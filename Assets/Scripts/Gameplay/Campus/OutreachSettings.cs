using UnityEngine;

namespace Systems.Campus
{
    [CreateAssetMenu(fileName = "OutreachSettings", menuName = "Campus/Outreach Settings")]
    public class OutreachSettings : ScriptableObject
    {
        public float weightPopularity = 0.35f;
        public float weightGrade = 0.25f;
        public float weightAffinity = 0.25f;
        public float weightInvites = 0.15f;

        [Tooltip("Added to named RSVP chance")]
        public float namedRsvpPerOutreach = 15f;

        [Tooltip("Added to filler archetype arrival chance")]
        public float fillerArrivalPerOutreach = 5f;

        public static float GradeWeight(Grade grade)
        {
            return grade switch
            {
                Grade.A => 1f,
                Grade.B => 0.85f,
                Grade.C => 0.65f,
                Grade.D => 0.45f,
                Grade.E => 0.3f,
                Grade.F => 0.2f,
                _ => 0.5f
            };
        }

        /// <summary>Party filler arrival bias — same grade ladder as <see cref="FraternityGradeCalculator.ArrivalChanceModifier"/>.</summary>
        public static float ArrivalChanceModifier(Grade grade) => FraternityGradeCalculator.ArrivalChanceModifier(grade);

        public float ComputeOutreach(float popularity, Grade grade, float avgAffinity, int invitesThisWeek)
        {
            float pop = Mathf.Clamp01((popularity + 100f) / 200f);
            float aff = Mathf.Clamp01((avgAffinity + 100f) / 200f);
            float inv = Mathf.Clamp01(invitesThisWeek / 10f);
            return weightPopularity * pop
                   + weightGrade * GradeWeight(grade)
                   + weightAffinity * aff
                   + weightInvites * inv;
        }

        public static OutreachSettings GetOrDefault()
        {
            var settings = Resources.Load<OutreachSettings>("Campus/OutreachSettings");
            if (settings != null)
            {
                return settings;
            }

            return CreateInstance<OutreachSettings>();
        }
    }

    public static class AffinityPartyModifiers
    {
        public static float TroubleOffset(float affinity)
        {
            // affinity -100..100 → trouble offset +30..-20
            return Mathf.Lerp(30f, -20f, Mathf.InverseLerp(-100f, 100f, affinity));
        }

        public static float PopularityOffset(float affinity)
        {
            return Mathf.Lerp(-15f, 15f, Mathf.InverseLerp(-100f, 100f, affinity));
        }

        public static float RsvpBonus(float affinity)
        {
            return Mathf.Lerp(-40f, 40f, Mathf.InverseLerp(-100f, 100f, affinity));
        }
    }
}
