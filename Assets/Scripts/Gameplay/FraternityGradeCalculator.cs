using UnityEngine;

/// <summary>
/// Converts team metrics into a 0–100 rating and letter <see cref="Grade"/>.
/// Trouble is not a grade penalty — survived chaos boosts popularity separately.
/// </summary>
public static class FraternityGradeCalculator
{
    public const float WeightPopularity = 0.50f;
    public const float WeightCash = 0.30f;
    public const float WeightMembers = 0.20f;

    /// <summary>Fraction of party trouble converted to pop when the night is survived.</summary>
    public const float SurviveChaosFactor = 0.15f;
    public const float SurviveChaosMaxBoost = 25f;

    public const float PopNormalizeSpan = 200f;
    public const float CashNormalizeSpan = 500f;
    public const float MembersNormalizeSpan = 20f;

    public static int ComputeRating(float totalPopularity, float totalCashValue, int memberCount)
    {
        float popN = NormalizeMetric(totalPopularity, PopNormalizeSpan);
        float cashN = NormalizeMetric(totalCashValue, CashNormalizeSpan);
        float membersN = NormalizeMetric(memberCount, MembersNormalizeSpan);
        float rating = WeightPopularity * popN + WeightCash * cashN + WeightMembers * membersN;
        return Mathf.Clamp(Mathf.RoundToInt(rating), 0, 100);
    }

    public static Grade GradeFromRating(int rating)
    {
        if (rating >= 85) return Grade.A;
        if (rating >= 70) return Grade.B;
        if (rating >= 55) return Grade.C;
        if (rating >= 40) return Grade.D;
        if (rating >= 25) return Grade.E;
        return Grade.F;
    }

    public static string ToLetter(Grade grade)
    {
        return grade switch
        {
            Grade.A => "A",
            Grade.B => "B",
            Grade.C => "C",
            Grade.D => "D",
            Grade.E => "E",
            Grade.F => "F",
            _ => "?"
        };
    }

    /// <summary>True when FraternityData is still the default struct (Grade A / rating 0).</summary>
    public static bool IsUninitialized(in FraternityData data)
    {
        // Grade.A shares value 0 with Grade.First — both mean "never graded" here.
        return data.rating == 0
               && data.popularity == 0
               && data.cashValue == 0
               && (data.grade == Grade.A || (int)data.grade == 0);
    }

    public static FraternityData CreateStarting(string fratName)
    {
        return new FraternityData(
            string.IsNullOrWhiteSpace(fratName) ? "Fraternity" : fratName,
            rating: 50,
            grade: Grade.C,
            memberCount: 0,
            popularity: 0,
            cashValue: 0);
    }

    public static void ApplyToFraternityData(
        ref FraternityData data,
        float totalPopularity,
        float totalCashValue,
        int memberCount)
    {
        data.memberCount = Mathf.Clamp(memberCount, 0, FraternityData.MAX_MEMBERS_ALLOWED);
        data.popularity = Mathf.RoundToInt(totalPopularity);
        data.cashValue = Mathf.RoundToInt(totalCashValue);

        // No season progress yet — keep the seeded mid-pack grade (C / 50), not F / 0.
        bool noProgress = Mathf.Approximately(totalPopularity, 0f)
                          && Mathf.Approximately(totalCashValue, 0f)
                          && data.memberCount <= 0;
        if (noProgress)
        {
            data.rating = 50;
            data.grade = Grade.C;
            return;
        }

        data.rating = ComputeRating(totalPopularity, totalCashValue, data.memberCount);
        data.grade = GradeFromRating(data.rating);
    }

    /// <summary>Slight popularity reward for finishing a chaotic party.</summary>
    public static float SurviveChaosBoost(float partyTrouble)
    {
        if (partyTrouble <= 0f)
        {
            return 0f;
        }

        return Mathf.Min(partyTrouble * SurviveChaosFactor, SurviveChaosMaxBoost);
    }

    /// <summary>Additive arrival chance modifier by frat grade (shared with campus outreach curve).</summary>
    public static float ArrivalChanceModifier(Grade grade)
    {
        return grade switch
        {
            Grade.A => 10f,
            Grade.B => 5f,
            Grade.C => 0f,
            Grade.D => -5f,
            Grade.E => -10f,
            Grade.F => -15f,
            _ => 0f
        };
    }

    static float NormalizeMetric(float value, float span)
    {
        if (span <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(value / span) * 100f;
    }
}
