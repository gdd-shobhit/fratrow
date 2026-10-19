using System;
using FratRow.Social;

namespace FratRow.Game
{
    /// <summary>
    /// Brother drinking and drunkenness (design/gdd/drunk-mechanic.md). Host-authoritative like the rest of the session:
    /// drinking draws from the same house stash and open containers as the party guests, intoxication sobers off in real
    /// seconds, and past <see cref="DrunkTuning.threshold"/> everything the brother says is rewritten by <see cref="DrunkSpeech"/>.
    /// Example: <c>session.Apply(new PlayerAction { playerId = id, action = ActionIds.Drink, item = "keg" });</c>
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>Intoxication as a 0..1 share of <see cref="DrunkTuning.max"/> (HUD bar, gameplay penalties).</summary>
        public float Drunk01(PlayerState p) => p == null || Config.drunk.max <= 0 ? 0f : Clamp(p.drunk / Config.drunk.max, 0, 1);

        /// <summary>What other people can see: <see cref="SpeakerConditions"/> sober, tipsy, drunk or wasted.</summary>
        public string DrunkCondition(PlayerState p)
        {
            var t = Config.drunk;
            if (p == null || p.drunk < t.tipsyAt || p.drunk <= 0) return SpeakerConditions.Sober;
            if (p.drunk < t.threshold) return SpeakerConditions.Tipsy;
            return p.drunk < t.wastedAt ? SpeakerConditions.Drunk : SpeakerConditions.Wasted;
        }

        /// <summary>0 sober, 1 tipsy, 2 drunk, 3 wasted (compact replication of <see cref="DrunkCondition"/>).</summary>
        public static byte ConditionTier(string condition) =>
            condition == SpeakerConditions.Tipsy ? (byte)1 : condition == SpeakerConditions.Drunk ? (byte)2 : condition == SpeakerConditions.Wasted ? (byte)3 : (byte)0;

        /// <summary>How hard this brother's speech is rewritten right now (0 below the threshold).</summary>
        public float SpeechIntensity(PlayerState p) => p == null ? 0f : DrunkSpeech.Intensity(p.drunk, Config.drunk);

        /// <summary>The line as this brother actually says it. Sober speech is returned untouched and consumes no randomness.</summary>
        string SpokenText(PlayerState p, string text, bool cosmeticOnly)
        {
            float intensity = SpeechIntensity(p);
            if (intensity <= 0) return text;
            return DrunkSpeech.Rewrite(text, intensity, Config.drunk.speech, NextRng("drunk-speech"), cosmeticOnly);
        }

        ActionResult Drink(PlayerState p, PlayerAction a)
        {
            var t = Config.drunk;
            if (InPhase(Phase.SemesterEnd)) return ActionResult.Fail("wrong_phase", "The semester is over.");
            if (p.location != Config.playerHomeLocation) return ActionResult.Fail("wrong_location", "The drinks are back at the house.");
            ShopItemDef wanted = null;
            if (!string.IsNullOrEmpty(a.item))
            {
                wanted = Config.Item(a.item);
                if (wanted == null || wanted.category != "drink" || wanted.servings <= 0) return ActionResult.Fail("invalid_target", "That's not a drink.");
            }
            if ((wanted == null || wanted.alcoholic) && p.drunk >= t.max) return ActionResult.Fail("cap_reached", "You can't stomach another drop.");
            if (State.time < p.nextDrinkAt) return ActionResult.Fail("cap_reached", "Pace yourself.");
            var item = TakeServing(PlayerHouse, allowAlcohol: true, only: wanted);
            if (item == null) return ActionResult.Fail("missing_item", "The house is out of " + (wanted?.name ?? "drinks") + ". Buy more at the campus store.");
            p.nextDrinkAt = State.time + t.drinkCooldownSeconds;
            if (!item.alcoholic || item.strength <= 0) return ActionResult.Ok("You grab some " + item.name + ". Very responsible.");

            string before = DrunkCondition(p);
            p.drunk = Math.Min(t.max, p.drunk + item.strength);
            string now = DrunkCondition(p);
            if (State.phase == Phase.Party && State.party.active && t.partyHype > 0)
                State.party.hype = Clamp(State.party.hype + t.partyHype, 0, 100);
            // The whole house hears about it (news is broadcast to every brother).
            if (now != before && SpeakerConditions.IsNoticeable(now))
                News("drunk", p.displayName + (now == SpeakerConditions.Wasted ? " is absolutely wasted." : " is getting drunk."), PlayerHouse.id, p.playerId);
            return ActionResult.Ok("You drink from the " + item.name + ". " + Feeling(now));
        }

        static string Feeling(string condition)
        {
            switch (condition)
            {
                case SpeakerConditions.Tipsy: return "You're feeling it.";
                case SpeakerConditions.Drunk: return "Everything is hilarious.";
                case SpeakerConditions.Wasted: return "The room is spinning.";
                default: return "Smooth.";
            }
        }

        /// <summary>Every brother sobers at <see cref="DrunkTuning.soberPerSecond"/>, online or not.</summary>
        void SoberUp(double dt)
        {
            float amount = (float)(Config.drunk.soberPerSecond * dt);
            if (amount <= 0) return;
            foreach (var p in State.players)
                if (p.drunk > 0) p.drunk = Math.Max(0f, p.drunk - amount);
        }
    }
}
