using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FratRow.Social
{
    public sealed class SocialConversationService : IDisposable
    {
        readonly SocialWorld world;
        readonly IStateCodec codec;
        readonly ISocialModelProvider model;
        readonly IInteractionAccess access;
        readonly SemaphoreSlim inference = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, SemaphoreSlim> mailboxes = new Dictionary<string, SemaphoreSlim>();
        readonly HashSet<string> pendingPlayers = new HashSet<string>();
        sealed class Flight
        {
            public string fingerprint;
            public TaskCompletionSource<ConversationResult> completion = new TaskCompletionSource<ConversationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        readonly Dictionary<string, Flight> flights = new Dictionary<string, Flight>();
        readonly object queueLock = new object();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        int pending;
        bool disposed;
        readonly TimeSpan generationTimeout;
        readonly bool includeGameSetting;
        readonly bool requireGroundedProposal;
        public SocialConversationService(SocialWorld world, IStateCodec codec, ISocialModelProvider model, IInteractionAccess access, TimeSpan? generationTimeout = null, bool includeGameSetting = true, bool requireGroundedProposal = false)
        { this.world = world; this.codec = codec; this.model = model; this.access = access; this.generationTimeout = generationTimeout ?? TimeSpan.FromSeconds(15); this.includeGameSetting = includeGameSetting; this.requireGroundedProposal = requireGroundedProposal; }

        public async Task<ConversationResult> SubmitAsync(string playerId, ConversationRequest input, CancellationToken cancelQueued = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SocialConversationService));
            if (input == null) throw new ArgumentNullException(nameof(input));
            // Do not retain a caller-mutable request while it is queued.
            var request = codec.Decode<ConversationRequest>(codec.Encode(input));
            ValidateInput(playerId, request);
            var recorded = FindTurn(world.Store.Read(), playerId, request.requestId);
            if (recorded != null && codec.Encode(recorded.request) != codec.Encode(request))
                throw new InvalidOperationException("Request ID was already used for different speech");
            if (recorded?.result?.status == "committed") return recorded.result;
            SemaphoreSlim mailbox;
            string flightKey = playerId + "/" + request.requestId;
            Flight flight;
            bool duplicate;
            lock (queueLock)
            {
                duplicate = flights.TryGetValue(flightKey, out flight);
                if (duplicate)
                {
                    if (flight.fingerprint != codec.Encode(request)) throw new InvalidOperationException("Request ID already used for different queued speech");
                }
                else
                {
                    if (pending >= SocialRules.QueueCapacity || !pendingPlayers.Add(playerId))
                        return Result(request, "busy", "One outstanding turn per player; queue is bounded");
                    pending++;
                    flights.Add(flightKey, flight = new Flight { fingerprint = codec.Encode(request) });
                }
                if (!mailboxes.TryGetValue(request.npcId, out mailbox))
                    mailboxes.Add(request.npcId, mailbox = new SemaphoreSlim(1, 1));
            }
            if (duplicate) return await flight.completion.Task;
            ConversationResult Finish(ConversationResult result) { flight.completion.TrySetResult(result); return result; }
            bool enteredMailbox = false;
            bool enteredInference = false;
            using (var queued = CancellationTokenSource.CreateLinkedTokenSource(cancelQueued, lifetime.Token))
            {
                try
                {
                    await mailbox.WaitAsync(queued.Token); enteredMailbox = true;
                    // Waiting for the shared runtime is still unobserved and can be canceled.
                    await inference.WaitAsync(queued.Token); enteredInference = true;
                    queued.Token.ThrowIfCancellationRequested();
                    var s = world.Store.Read();
                    var turn = FindTurn(s, playerId, request.requestId);
                    if (turn?.result?.status == "committed") return Finish(turn.result);
                    if (turn == null)
                    {
                        if (!access.CanHear(playerId, request)) return Finish(Result(request, "rejected", "NPC cannot receive this interaction"));
                        await Task.Run(() => world.Store.Transact(next =>
                        {
                            var npc = SocialWorld.Npc(next, request.npcId);
                            var receipt = SocialWorld.AddEvent(next, "utterance", "speech", playerId, npc.npcId,
                                request.channel, request.text, new List<string> { npc.npcId });
                            npc.revision++;
                            next.turns.Add(new TurnRecord { playerId = playerId, request = request, observedEventId = receipt.eventId,
                                result = Result(request, "pending", null) });
                        }));
                    }
                    // After receipt, caller cancellation/disconnect cannot erase the utterance.
                    string repair = null;
                    using (var generation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                    {
                        generation.CancelAfter(generationTimeout);
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            try
                            {
                                s = world.Store.Read(); turn = FindTurn(s, playerId, request.requestId);
                                var context = SocialMemory.Build(s, turn, codec, repair, includeGameSetting);
                                SocialProposal proposal;
                                try { proposal = await model.GenerateAsync(context, generation.Token); }
                                catch (System.IO.IOException e) { repair = "Model file/runtime unavailable: " + e.Message; break; }
                                generation.Token.ThrowIfCancellationRequested();
                                if (requireGroundedProposal && proposal?.grounded == null) throw new ArgumentException("Host requires grounded social acts");
                                ValidateProposal(proposal, context);
                                ConversationResult committed = null;
                                await Task.Run(() => world.Store.Transact(next => committed = Commit(next, playerId, request, context, proposal)));
                                return Finish(committed);
                            }
                            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is StaleSocialStateException)
                            { repair = e.Message; }
                            catch (OperationCanceledException) { repair = "Generation interrupted; received speech retained"; break; }
                            catch (Exception e) when (!(e is System.IO.IOException))
                            { repair = "Model unavailable: " + e.GetType().Name + ": " + e.Message; break; }
                        }
                    }
                    // Failure is recorded for inspection. A retry with this same request ID reuses the receipt.
                    var failed = Result(request, "pending", repair ?? "Decision could not be validated");
                    failed.reply = "Conversation interrupted. Please try again.";
                    await Task.Run(() => world.Store.Transact(next => FindTurn(next, playerId, request.requestId).result = failed));
                    return Finish(failed);
                }
                catch (OperationCanceledException) { return Finish(Result(request, "cancelled", "Queued input was not received")); }
                catch (Exception e) { flight.completion.TrySetException(e); throw; }
                finally
                {
                    if (enteredInference) inference.Release();
                    if (enteredMailbox) mailbox.Release();
                    lock (queueLock) { pending--; pendingPlayers.Remove(playerId); flights.Remove(flightKey); }
                }
            }
        }
        public async Task ResumeReceivedAsync()
        {
            foreach (var turn in world.Store.Read().turns.Where(t => t.result.status == "pending"))
            {
                if (lifetime.IsCancellationRequested) return;
                await SubmitAsync(turn.playerId, turn.request);
            }
        }
        void ValidateInput(string playerId, ConversationRequest request)
        {
            var s = world.Store.Read();
            if (!s.players.Any(p => p.playerId == playerId)) throw new UnauthorizedAccessException("Unknown bound player");
            if (string.IsNullOrWhiteSpace(request.requestId) || request.requestId.Length > 64 ||
                string.IsNullOrWhiteSpace(request.text) || request.text.Length > SocialRules.InputCharacters ||
                (request.channel != "text" && request.channel != "in_person") || !SpeakerConditions.IsValid(request.speakerCondition) ||
                SocialWorld.Npc(s, request.npcId) == null)
                throw new ArgumentException("Invalid conversation request");
            if (!string.IsNullOrEmpty(request.partyId) && SocialWorld.Party(s, request.partyId) == null)
                throw new ArgumentException("Unknown party");
        }
        internal static TurnRecord FindTurn(CampaignState s, string player, string id) =>
            s.turns.Find(t => t.playerId == player && t.request.requestId == id);
        static ConversationResult Result(ConversationRequest r, string status, string error) =>
            new ConversationResult { requestId = r.requestId, status = status, error = error };
        public static void ValidateProposal(SocialProposal p, ModelContext c, bool requireReply = true)
        {
            if (p == null || p.schema != SocialRules.Schema || !SocialRules.IsDecision(p.decision) ||
                (requireReply && string.IsNullOrWhiteSpace(p.reply)) || (p.reply != null && p.reply.Length > 300) || string.IsNullOrWhiteSpace(p.interpretation) || p.interpretation.Length > 160)
                throw new ArgumentException("Invalid schema, decision or reply length");
            if (p.evidence_ids == null || p.evidence_ids.Length == 0 || p.evidence_ids.Length > 4 ||
                p.evidence_ids.Any(e => !c.EvidenceIds.Contains(e))) throw new ArgumentException("Evidence not present in NPC context");
            if (p.relationship_effects == null || p.relationship_effects.Length > 2)
                throw new ArgumentException("At most two relationship effects");
            var scopes = new HashSet<string>();
            foreach (var effect in p.relationship_effects)
            {
                if (effect == null || !scopes.Add(effect.scope) ||
                    !((effect.scope == "player" && effect.id == c.PlayerId) || (effect.scope == "house" && effect.id == c.FraternityId)))
                    throw new ArgumentException("Invalid relationship target");
                SocialRules.Delta(effect.magnitude);
            }
            if (p.decision != "respond" && (c.PartyId == null || (c.PartyState != "planned" && c.PartyState != "active")))
                throw new ArgumentException("Invitation requires a live party");
            if (p.condition != (p.decision == "counteroffer" ? SocialRules.EntranceCondition : "none"))
                throw new ArgumentException("Unknown or contradictory condition");
            // The typed grievance field is itself an evidence reference. Do not require the
            // model to redundantly repeat the same known ID in the general evidence array.
            if (!string.IsNullOrEmpty(p.grievance_event_id) && !c.GrievanceCandidateIds.Contains(p.grievance_event_id))
                throw new ArgumentException("Grievance evidence not present in NPC context");
            if (!string.IsNullOrEmpty(p.resolve_grievance_event_id) && !c.GrievanceIds.Contains(p.resolve_grievance_event_id))
                throw new ArgumentException("Cannot resolve unknown grievance");
            if (p.grounded != null) GroundedSocialTurn.Validate(c, p);
        }
        ConversationResult Commit(CampaignState s, string player, ConversationRequest request, ModelContext context, SocialProposal proposal)
        {
            var turn = FindTurn(s, player, request.requestId);
            if (turn.result.status == "committed") return turn.result;
            var npc = SocialWorld.Npc(s, request.npcId);
            var party = SocialWorld.Party(s, request.partyId);
            if (npc.revision != context.NpcRevision || (party?.revision ?? -1) != context.PartyRevision ||
                SocialStanding.Revision(s) != context.StandingRevision)
                throw new StaleSocialStateException();
            ValidateProposal(proposal, context);
            var commitment = npc.commitments.Find(c => c.partyId == request.partyId);
            if ((proposal.decision == "accept" || proposal.decision == "counteroffer") && party != null)
            {
                if (commitment?.arrived == true) throw new InvalidOperationException("NPC already attended this party");
                int reserved = s.npcs.Count(n => n.npcId != npc.npcId && n.commitments.Any(c => c.partyId == party.partyId && (c.state == "accepted" || c.state == "conditional")));
                if (reserved >= party.capacity) throw new InvalidOperationException("Party has no remaining reserved places");
                if (proposal.decision == "counteroffer" && party.state != "planned")
                    throw new InvalidOperationException("Entrance counteroffers must be agreed before party start");
            }
            if (commitment?.arrived == true && proposal.decision != "respond")
                throw new InvalidOperationException("Attendance already fulfilled; use conversation instead");
            var decisionEvent = SocialWorld.AddEvent(s, "social_decision", "inference", npc.npcId, player, request.channel,
                codec.Encode(proposal), new List<string> { npc.npcId });
            decisionEvent.relatedEventId = turn.observedEventId;
            var impact = new TurnImpact { previousCommitment = commitment?.state ?? "" };
            foreach (var effect in proposal.relationship_effects)
            {
                int delta = BudgetedDelta(s, npc.npcId, player, request.partyId, effect.scope, SocialRules.Delta(effect.magnitude));
                if (effect.scope == "house") { npc.fraternityRegard = Clamp(npc.fraternityRegard + delta); impact.regard += delta; }
                else
                {
                    var trust = npc.trust.Find(t => t.playerId == player);
                    if (trust == null) npc.trust.Add(trust = new PersonalTrust { playerId = player });
                    trust.value = Clamp(trust.value + delta);
                    impact.trust += delta;
                }
            }
            if (!string.IsNullOrEmpty(proposal.grievance_event_id) && !npc.grievances.Any(g => g.eventId == proposal.grievance_event_id))
            {
                impact.grievance = true;
                var cause = s.events.Find(e => e.eventId == proposal.grievance_event_id);
                var original = SocialWorld.GossipOrigin(s, cause);
                npc.grievances.Add(new Grievance { eventId = cause.eventId, actorId = original?.actorId ?? cause.actorId,
                    fraternityId = cause.fraternityId, knowledge = cause.source == "rumor" ? "reported" : "witnessed",
                    reporterId = cause.source == "rumor" ? cause.actorId : null, interpretation = proposal.interpretation });
            }
            if (!string.IsNullOrEmpty(proposal.resolve_grievance_event_id))
                npc.grievances.Find(g => g.eventId == proposal.resolve_grievance_event_id).resolved = true;
            if (proposal.decision != "respond")
            {
                if (commitment == null) npc.commitments.Add(commitment = new Commitment { partyId = request.partyId });
                commitment.inviterId = player;
                commitment.state = proposal.decision == "accept" ? "accepted" : proposal.decision == "counteroffer" ? "conditional" : proposal.decision == "decline" ? "declined" : "deferred";
                commitment.condition = proposal.condition; commitment.conditionResolved = false;
                commitment.decisionEventId = decisionEvent.eventId;
                party.revision++; // capacity and commitments are part of the party snapshot
            }
            npc.revision++;
            impact.commitment = commitment?.state ?? "";
            turn.impact = impact;
            return turn.result = new ConversationResult { requestId = request.requestId, status = "committed", reply = proposal.reply,
                decision = proposal.decision, commitment = commitment?.state, revision = npc.revision };
        }
        static int Clamp(int value) => Math.Max(-100, Math.Min(100, value));
        static int BudgetedDelta(CampaignState s, string npc, string player, string party, string scope, int proposed)
        {
            string dailyKey = npc + "/" + scope + "/day/" + s.day;
            string episodeKey = npc + "/" + player + "/" + scope + "/episode/" + (party ?? "day" + s.day);
            var daily = s.influence.Find(c => c.key == dailyKey);
            if (daily == null) s.influence.Add(daily = new InfluenceCounter { key = dailyKey });
            var episode = s.influence.Find(c => c.key == episodeKey);
            if (episode == null) s.influence.Add(episode = new InfluenceCounter { key = episodeKey });
            int amount = Math.Max(0, Math.Min(Math.Abs(proposed), Math.Min(SocialRules.DailyInfluence - daily.used, SocialRules.EpisodeInfluence - episode.used)));
            daily.used += amount; episode.used += amount;
            return Math.Sign(proposed) * amount;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); model.Dispose();
            // Gates are retained until any in-flight continuations finish releasing them.
        }
    }
}
