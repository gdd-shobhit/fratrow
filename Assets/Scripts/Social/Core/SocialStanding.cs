using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FratRow.Social
{
    public enum StatusConcern { Low = 0, Moderate = 1, High = 2 }
    [Serializable] public sealed class FraternityStanding
    {
        public bool known;
        public string name;
        public float popularity;
        public string grade;
        public int rating;
        public long revision;
    }
    [Serializable] public sealed class NpcStanding
    {
        public bool known;
        public float popularity;
        public StatusConcern statusConcern = StatusConcern.Moderate;
    }
    public static class SocialStanding
    {
        public const string Guidance = "Social standing matters: compare the NPC's public popularity with the fraternity's, alongside personal trust, house regard, persona and actual memories. " +
            "A status-conscious popular student may be selective about an unfamiliar low-profile house, ask who the inviter is, or need a credible reason to go. " +
            "A friendly student or established friend can care more about connection than status. High popularity never forces snobbery, acceptance, refusal or forgiveness. " +
            "Use only confirmed public guests when judging a party's social draw; an inviter's claims are not confirmed RSVPs. " +
            "Low popularity does not prove past bad parties or mistreatment. Familiarity follows actual contact; a supplied player display name does not mean you have met. " +
            "Popularity is a public game score, separate from your personal opinion of this fraternity. Never recite scores or backend terms in dialogue.";
        public static void Validate(float popularity)
        { if (float.IsNaN(popularity) || float.IsInfinity(popularity)) throw new ArgumentException("Invalid popularity"); }
        static string Number(float value) => value.ToString("G", CultureInfo.InvariantCulture);
        public static string Render(CampaignState s, NpcState npc, TurnRecord turn, IStateCodec codec)
        {
            var house = s.standing; var own = npc.standing;
            var text = new StringBuilder("PUBLIC SOCIAL STANDING (not personal trust):\n");
            text.AppendLine(house?.known == true ? "FRATERNITY " + s.fraternityId + "; name=" + codec.Encode(house.name) +
                "; popularity=" + Number(house.popularity) + (string.IsNullOrEmpty(house.grade) ? "" : "; grade=" + house.grade + "; rating=" + house.rating) : "FRATERNITY popularity unknown");
            text.AppendLine(own?.known == true ? "NPC " + npc.npcId + "; popularity=" + Number(own.popularity) : "NPC popularity unknown");
            if (house?.known == true && own?.known == true)
                text.AppendLine("NPC popularity is " + (own.popularity > house.popularity ? "higher than" : own.popularity < house.popularity ? "lower than" : "equal to") + " the fraternity's popularity on the supplied game scale.");
            var concern = own?.statusConcern ?? StatusConcern.Moderate;
            text.AppendLine("NPC STATUS PRIORITY = " + concern + (concern == StatusConcern.High ? "; strongly weighs prestige and social fit" :
                concern == StatusConcern.Low ? "; usually values treatment and connection over prestige" : "; weighs status alongside personality and relationships"));
            bool previous = s.events.Any(e => e.eventId != turn.observedEventId && e.source == "speech" && e.actorId == turn.playerId && e.targetId == npc.npcId && e.witnesses.Contains(npc.npcId));
            text.AppendLine("CURRENT_SPEAKER FAMILIARITY = " + (previous ? "prior direct conversation recorded" : "no prior direct conversation recorded; do not invent an introduction or friendship"));
            var party = SocialWorld.Party(s, turn.request.partyId);
            if (party != null && (party.state == "planned" || party.state == "active"))
            {
                var guests = s.npcs.Where(n => n.npcId != npc.npcId && n.commitments.Any(c => c.partyId == party.partyId && c.state == "accepted"))
                    .OrderByDescending(n => n.standing?.known == true ? n.standing.popularity : float.MinValue).ToList();
                text.AppendLine("PUBLIC CONFIRMED OTHER GUESTS = " + guests.Count + "; listed RSVPs are public commitments, not proof of arrival. Conditional/unconfirmed guests are excluded.");
                foreach (var guest in guests.Take(6))
                {
                    var commitment = guest.commitments.Find(c => c.partyId == party.partyId && c.state == "accepted");
                    text.AppendLine("GUEST " + codec.Encode(guest.npcId) + "; " + (guest.standing?.known == true ? "popularity=" + Number(guest.standing.popularity) : "popularity unknown") +
                        "; " + (commitment.arrived ? "arrived" : "RSVP accepted; not arrived"));
                }
            }
            return text.ToString();
        }
    }
}
