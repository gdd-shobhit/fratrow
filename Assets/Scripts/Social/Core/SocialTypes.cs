using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FratRow.Social
{
    public static class SocialRules
    {
        public const int Schema = 1;
        public const int InputCharacters = 800;
        public const int QueueCapacity = 16;
        public const int DailyInfluence = 20;
        public const int EpisodeInfluence = 10;
        public const string EntranceCondition = "inviter_at_entrance";
        public static bool IsDecision(string value) => value == "respond" || value == "accept" ||
            value == "decline" || value == "defer" || value == "counteroffer";
        public static int Delta(string value)
        {
            switch (value)
            {
                case "down": return -5;
                case "down_small": return -2;
                case "unchanged": return 0;
                case "up_small": return 2;
                case "up": return 5;
                default: throw new ArgumentException("Unknown influence magnitude");
            }
        }
    }

    [Serializable] public sealed class CampaignState
    {
        public int schema = SocialRules.Schema;
        public string campaignId;
        public string fraternityId;
        public long sequence;
        public int day;
        public bool legacyImported;
        public FraternityStanding standing;
        /// <summary>Public standings of other (AI) fraternities. Additive save field; older campaigns load empty.</summary>
        public List<RivalStanding> rivals = new List<RivalStanding>();
        public long rivalsRevision;
        /// <summary>The players' house's public campus rank (1 = top); 0 = unknown.</summary>
        public int playersRank;
        public List<PlayerIdentity> players = new List<PlayerIdentity>();
        public List<NpcState> npcs = new List<NpcState>();
        public List<SocialEvent> events = new List<SocialEvent>();
        public List<TurnRecord> turns = new List<TurnRecord>();
        public List<PartyRecord> parties = new List<PartyRecord>();
        public List<InfluenceCounter> influence = new List<InfluenceCounter>();
    }
    [Serializable] public sealed class PlayerIdentity
    {
        public string playerId;
        public string credentialHash;
        public string displayName;
    }
    [Serializable] public sealed class PlayerCredential
    {
        public string campaignId;
        public string playerId;
        public string token;
    }
    [Serializable] public sealed class NpcState
    {
        public string npcId;
        public string persona;
        public int personaVersion = 1;
        public NpcVoiceProfile voice;
        public int voiceVersion;
        public NpcStanding standing;
        /// <summary>"" = unaffiliated guest; "players" = brother of the players' house; otherwise a rival house display name.</summary>
        public string affiliation = "";
        public long revision;
        public int fraternityRegard;
        public List<PersonalTrust> trust = new List<PersonalTrust>();
        public List<Grievance> grievances = new List<Grievance>();
        public List<Commitment> commitments = new List<Commitment>();
    }
    /// <summary>Public campus standing of another fraternity, rendered as context only (no relationship effects target it).</summary>
    [Serializable] public sealed class RivalStanding
    {
        public string houseId;
        public string name;
        public float popularity;
        public string grade = "";
        public int rank;
    }
    [Serializable] public sealed class PersonalTrust { public string playerId; public int value; }
    [Serializable] public sealed class KnownPerson { public string playerId; public string displayName; }
    [Serializable] public sealed class Grievance
    {
        public string eventId, actorId, fraternityId, knowledge, reporterId;
        public string interpretation;
        public bool resolved;
    }
    [Serializable] public sealed class InfluenceCounter { public string key; public int used; }
    [Serializable] public sealed class SocialEvent
    {
        public string eventId;
        public string kind;
        public string source;
        public string actorId;
        public string targetId;
        public string fraternityId;
        public string location;
        public string text;
        public string relatedEventId;
        public int day;
        public long utcTicks;
        public List<string> witnesses = new List<string>();
        /// <summary>Gossip receipts only: who the story is about as the teller named them (a player id, "players" for the
        /// players' house, or a rival house name); "" when the teller didn't say. Additive save field.</summary>
        public string subjectId = "";
        /// <summary>Gossip receipts only: +1 flattering, -1 damaging for <see cref="subjectId"/>.</summary>
        public int valence;
    }
    /// <summary>One NPC-to-NPC retelling for <see cref="SocialWorld.ShareGossip"/>. <c>viaEventId</c> links the teller's own record
    /// (only for full-detail retellings); empty means an unlinked paraphrase.</summary>
    [Serializable] public sealed class GossipShare
    {
        /// <summary>Optional caller-assigned id (≤ 64 chars). Recording an id that already exists is a no-op (idempotent replay).</summary>
        public string eventId;
        public string tellerId, listenerId, viaEventId, text, subjectId;
        public int valence;
    }
    /// <summary>A world observation recorded in the same transaction as gossip shares (story origins, overheard lines, denials).</summary>
    [Serializable] public sealed class GossipObservation
    {
        /// <summary>Optional caller-assigned id, as for <see cref="GossipShare.eventId"/>.</summary>
        public string eventId;
        public string kind, actorId, targetId, text, location;
        public List<string> witnesses = new List<string>();
    }
    [Serializable] public sealed class PartyRecord
    {
        public string partyId;
        public string state = "planned";
        public long revision;
        public int capacity;
        public double startedAt;
    }
    [Serializable] public sealed class Commitment
    {
        public string partyId;
        public string inviterId;
        public string state;
        public string condition;
        public string decisionEventId;
        public bool conditionResolved;
        public bool arrived;
    }
    [Serializable] public sealed class PublicCommitment
    {
        public string npcId, partyId, inviterId, state, condition;
        public bool arrived;
    }
    [Serializable] public sealed class ConversationRequest
    {
        public string requestId;
        public string npcId;
        public string text;
        public string channel = "text";
        public string partyId;
    }
    [Serializable] public sealed class ConversationResult
    {
        public string requestId;
        public string status;
        public string reply;
        public string decision;
        public string commitment;
        public string error;
        public long revision;
    }
    [Serializable] public sealed class TurnRecord
    {
        public string playerId;
        public ConversationRequest request;
        public string observedEventId;
        public ConversationResult result;
        /// <summary>Host-only: what the committed turn actually changed. Additive save field; null for older turns.</summary>
        public TurnImpact impact;
    }
    /// <summary>Applied (post-budget) relationship deltas, grievance and commitment change of one committed turn.</summary>
    [Serializable] public sealed class TurnImpact
    {
        public int trust, regard;
        public bool grievance;
        public string previousCommitment = "", commitment = "";
    }
    [Serializable] public sealed class ConversationHistoryEntry
    {
        public ConversationRequest request;
        public ConversationResult result;
    }
    [Serializable] public sealed class RelationshipEffect
    {
        public string scope;
        public string id;
        public string magnitude;
    }
    [Serializable] public sealed class SocialProposal
    {
        public int schema;
        public string decision;
        public string[] evidence_ids;
        public RelationshipEffect[] relationship_effects;
        public string condition;
        public string grievance_event_id;
        public string resolve_grievance_event_id;
        public string interpretation;
        public string reply;
        public GroundedReply grounded;
    }
    [Serializable] public sealed class GroundedReply
    {
        public int version = 1;
        public string attitude, reason, topic;
        public int phrasing;
    }
    public sealed class GroundedMemory
    {
        public string eventId, actorId, actorName, knowledge, source;
    }
    public sealed class ModelContext
    {
        public string NpcId;
        public string PlayerId;
        public string FraternityId;
        public string Persona;
        public NpcVoiceProfile Voice;
        public int PersonaVersion;
        public long NpcRevision;
        public long StandingRevision;
        public long PartyRevision;
        public string PartyId;
        public string PartyState;
        public string ObservedEventId;
        public string Prompt;
        public string CompactPrompt;
        public string FactPrompt;
        public string SystemPrompt;
        public string GameSetting;
        public string DecisionFacts;
        public bool PriorDirectContact;
        public bool AttendanceFulfilled;
        public bool CanReserveAttendance = true;
        public int SamplingSeed;
        public List<GroundedMemory> GroundedMemories = new List<GroundedMemory>();
        public string RepairError;
        public HashSet<string> EvidenceIds = new HashSet<string>();
        public HashSet<string> GrievanceIds = new HashSet<string>();
        public HashSet<string> GrievanceCandidateIds = new HashSet<string>();
        public HashSet<string> KnownPlayerIds = new HashSet<string>();
        public Dictionary<string, string> KnownPlayerNames = new Dictionary<string, string>();
        public Dictionary<string, string> KnownPlayerAliases = new Dictionary<string, string>();
    }
    public interface IStateCodec
    {
        string Encode<T>(T value);
        T Decode<T>(string json);
    }
    public interface ISocialStore
    {
        CampaignState Read();
        /// <summary>
        /// The committed state itself, without the deep copy <see cref="Read"/> makes. Read-only: never mutate it. Commits publish a
        /// new object, so a reference obtained here never changes underneath the reader (safe across threads).
        /// </summary>
        CampaignState ReadShared();
        void Transact(Action<CampaignState> change);
    }
    public interface ISocialModelProvider : IDisposable
    {
        Task<SocialProposal> GenerateAsync(ModelContext context, CancellationToken cancellation);
    }
    public interface IInteractionAccess
    {
        bool CanHear(string playerId, ConversationRequest request);
    }
    public sealed class StaleSocialStateException : Exception
    {
        public StaleSocialStateException() : base("The NPC or party changed while generating") { }
    }
}
