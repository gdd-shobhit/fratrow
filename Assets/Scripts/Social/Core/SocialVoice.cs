using System;
using System.Linq;

namespace FratRow.Social
{
    [Serializable] public sealed class NpcVoiceProfile
    {
        public string generation = "Gen Z";
        public string role = "college student";
        public string gender = "unspecified";
        public string language = "English";
        public string tone = "casual";
        public string slang = "light";
        public string[] exampleLines = Array.Empty<string>();
    }

    /// <summary>Host-authored character voice; independent of social history and attendance decisions.</summary>
    public static class SocialVoice
    {
        [Serializable] sealed class Examples { public string[] lines; }
        public static NpcVoiceProfile Ashley() => SocialCharacterCatalog.Get("Ashley").voice;
        public static NpcVoiceProfile Copy(NpcVoiceProfile value)
        {
            var p = value ?? new NpcVoiceProfile();
            Validate(p);
            return new NpcVoiceProfile { generation = p.generation, role = p.role, gender = p.gender,
                language = p.language, tone = p.tone, slang = p.slang, exampleLines = p.exampleLines.ToArray() };
        }
        public static void Validate(NpcVoiceProfile p)
        {
            bool Text(string s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max;
            if (p == null || !Text(p.generation, 40) || !Text(p.role, 60) || !Text(p.gender, 40) || !Text(p.language, 40) ||
                !new[] { "casual", "assertive", "bubbly", "dry", "reserved", "playful", "laid-back" }.Contains(p.tone) ||
                !new[] { "none", "light", "frequent" }.Contains(p.slang) || p.exampleLines == null ||
                p.exampleLines.Length > 3 || p.exampleLines.Any(s => !Text(s, 160)))
                throw new ArgumentException("Invalid NPC voice profile");
        }
        public static string Prompt(NpcVoiceProfile value, IStateCodec codec)
        {
            var p = Copy(value);
            string identity = p.role == "college student" && (p.gender == "girl" || p.gender == "boy")
                ? p.generation + " college " + p.gender
                : p.generation + " " + p.role + (p.gender == "unspecified" ? "" : " (" + p.gender + ")");
            string tone = p.tone == "assertive" ? "casual, direct and self-respecting; push back clearly when someone is rude" :
                p.tone == "bubbly" ? "warm, upbeat and expressive, while keeping personal boundaries" :
                p.tone == "dry" ? "understated, concise and dryly witty; avoid overexplaining" :
                p.tone == "reserved" ? "quiet, cautious and brief; avoid forced enthusiasm" :
                p.tone == "playful" ? "light, teasing and conversational; take cruelty seriously" :
                p.tone == "laid-back" ? "relaxed, easygoing and brief; speak plainly" : "casual, friendly and straightforward";
            return "CHARACTER VOICE: You are a " + identity + ". Answer in " + p.language + ". Your tone is " + tone + ". " +
                (p.slang == "none" ? "Use everyday wording without slang. " : p.slang == "frequent" ?
                    "Use colloquial college slang naturally, without stacking catchphrases. " : "Use light, natural college slang and contractions; don't force memes or slang into every line. ") +
                "Write like a person chatting, not customer support or a therapist. Short, direct sentences. Don't thank someone for insulting you. " +
                "Your persona and the situation shape your reaction; age and gender do not dictate personality. " +
                (p.exampleLines.Length == 0 ? "" : "Voice examples ONLY, not past events or mandatory responses: " + codec.Encode(new Examples { lines = p.exampleLines }) + ". ") +
                "Style must preserve the chosen decision, actor attribution and known facts. Never add an attendance refusal just to match a voice example.";
        }
    }
}
