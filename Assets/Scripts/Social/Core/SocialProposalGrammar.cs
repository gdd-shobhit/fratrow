using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace FratRow.Social
{
    /// <summary>
    /// GBNF grammar for a full <see cref="SocialProposal"/> (the legacy free-form and compact formats). Host IDs are
    /// restricted to the turn's own evidence, player, house and grievances; semantic validation still happens in the host.
    /// Example: <c>string grammar = SocialProposalGrammar.GrammarFor(context);</c>
    /// </summary>
    public static class SocialProposalGrammar
    {
        // Field ordering and bounded arrays keep decoding compact. Semantic checks remain in host code.
        public const string Grammar = @"
root ::= ""{"" ws ""\""schema\"""" ws "":"" ws ""1"" ws "","" ws ""\""evidence_ids\"""" ws "":"" ws ids ws "","" ws ""\""interpretation\"""" ws "":"" ws str ws "","" ws ""\""decision\"""" ws "":"" ws decision ws "","" ws ""\""relationship_effects\"""" ws "":"" ws effects ws "","" ws ""\""condition\"""" ws "":"" ws condition ws "","" ws ""\""grievance_event_id\"""" ws "":"" ws str ws "","" ws ""\""resolve_grievance_event_id\"""" ws "":"" ws str ws "","" ws ""\""reply\"""" ws "":"" ws str ws ""}""
decision ::= ""\""respond\"""" | ""\""accept\"""" | ""\""decline\"""" | ""\""defer\"""" | ""\""counteroffer\""""
condition ::= ""\""none\"""" | ""\""inviter_at_entrance\""""
ids ::= ""["" ws str (ws "","" ws str){0,3} ws ""]""
effects ::= ""["" ws (effect (ws "","" ws effect)?)? ws ""]""
effect ::= ""{"" ws ""\""scope\"""" ws "":"" ws (""\""player\"""" | ""\""house\"""") ws "","" ws ""\""id\"""" ws "":"" ws str ws "","" ws ""\""magnitude\"""" ws "":"" ws magnitude ws ""}""
magnitude ::= ""\""down\"""" | ""\""down_small\"""" | ""\""unchanged\"""" | ""\""up_small\"""" | ""\""up\""""
str ::= ""\"""" char{0,300} ""\""""
char ::= [^""\\\x00-\x1f] | ""\\"" ([""\\/bfnrt] | ""u"" [0-9a-fA-F]{4})
ws ::= [ \t\n\r]{0,2}
";
        public static string GrammarFor(ModelContext context)
        {
            if (context == null || context.EvidenceIds.Count == 0) throw new ArgumentException("A model turn requires observed evidence");
            string grammar = Regex.Replace(Grammar, @"(?m)^ids ::= .*", "ids ::= \"[\" ws evidence (ws \",\" ws evidence){0,3} ws \"]\"");
            grammar += "\nevidence ::= " + string.Join(" | ", context.EvidenceIds.OrderBy(x => x).Select(JsonIdTerminal)) + "\n";
            string player = "\"{\" ws \"\\\"scope\\\"\" ws \":\" ws \"\\\"player\\\"\" ws \",\" ws \"\\\"id\\\"\" ws \":\" ws " + JsonIdTerminal(context.PlayerId);
            string house = "\"{\" ws \"\\\"scope\\\"\" ws \":\" ws \"\\\"house\\\"\" ws \",\" ws \"\\\"id\\\"\" ws \":\" ws " + JsonIdTerminal(context.FraternityId);
            string tail = " ws \",\" ws \"\\\"magnitude\\\"\" ws \":\" ws magnitude ws \"}\"";
            grammar += "player-effect ::= " + player + tail + "\nhouse-effect ::= " + house + tail + "\n";
            grammar = Regex.Replace(grammar, @"(?m)^effects ::= .*", "effects ::= \"[\" ws (player-effect (ws \",\" ws house-effect)? | house-effect)? ws \"]\"");
            grammar = grammar.Replace("\"\\\"grievance_event_id\\\"\" ws \":\" ws str", "\"\\\"grievance_event_id\\\"\" ws \":\" ws grievance-id")
                .Replace("\"\\\"resolve_grievance_event_id\\\"\" ws \":\" ws str", "\"\\\"resolve_grievance_event_id\\\"\" ws \":\" ws resolved-id")
                .Replace("\"\\\"interpretation\\\"\" ws \":\" ws str", "\"\\\"interpretation\\\"\" ws \":\" ws short-str")
                .Replace("\"\\\"reply\\\"\" ws \":\" ws str", "\"\\\"reply\\\"\" ws \":\" ws reply-str");
            grammar += "grievance-id ::= \"\\\"\\\"\"" + (context.GrievanceCandidateIds.Count == 0 ? "" : " | " + string.Join(" | ", context.GrievanceCandidateIds.OrderBy(x => x).Select(JsonIdTerminal))) + "\nresolved-id ::= \"\\\"\\\"\"" +
                (context.GrievanceIds.Count == 0 ? "" : " | " + string.Join(" | ", context.GrievanceIds.OrderBy(x => x).Select(JsonIdTerminal))) + "\n";
            grammar += "short-str ::= \"\\\"\" char{1,160} \"\\\"\"\nreply-str ::= \"\\\"\" char{1,300} \"\\\"\"\n";
            string root = Regex.Match(grammar, @"(?m)^root ::= .*").Value;
            string ordinary = root.Replace("root ::=", "ordinary ::=").Replace("ws condition ws", "ws \"\\\"none\\\"\" ws");
            string conditional = root.Replace("root ::=", "conditional ::=").Replace("ws decision ws", "ws \"\\\"counteroffer\\\"\" ws")
                .Replace("ws condition ws", "ws \"\\\"inviter_at_entrance\\\"\" ws");
            grammar = Regex.Replace(grammar, @"(?m)^root ::= .*", "root ::= ordinary" + (context.PartyState == "planned" ? " | conditional" : ""));
            grammar += ordinary + "\n" + conditional + "\n";
            grammar = Regex.Replace(grammar, @"(?m)^decision ::= .*", "decision ::= \"\\\"respond\\\"\" | \"\\\"accept\\\"\" | \"\\\"decline\\\"\" | \"\\\"defer\\\"\"");
            if (context.PartyId == null || (context.PartyState != "planned" && context.PartyState != "active"))
                grammar = Regex.Replace(grammar, @"(?m)^decision ::= .*", "decision ::= \"\\\"respond\\\"\"");
            return grammar;
        }
        static string JsonIdTerminal(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("Host ID is not grammar-safe");
            return "\"\\\"" + id + "\\\"\"";
        }
    }
}
