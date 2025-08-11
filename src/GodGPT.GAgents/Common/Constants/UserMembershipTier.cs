namespace Aevatar.Application.Grains.Common.Constants;

/// <summary>
/// User membership tier constants for analytics and telemetry
/// These are used for detailed user retention and conversion analysis
/// </summary>
public static class UserMembershipTier
{
    /// <summary>
    /// No active subscription
    /// </summary>
    public const string Free = "free";

    // Premium Tier Constants
    /// <summary>
    /// Premium daily subscription
    /// </summary>
    public const string PremiumDay = "premium_day";

    /// <summary>
    /// Premium weekly subscription
    /// </summary>
    public const string PremiumWeek = "premium_week";

    /// <summary>
    /// Premium monthly subscription
    /// </summary>
    public const string PremiumMonth = "premium_month";

    /// <summary>
    /// Premium yearly subscription
    /// </summary>
    public const string PremiumYear = "premium_year";

    // Ultimate Tier Constants
    /// <summary>
    /// Ultimate daily subscription
    /// </summary>
    public const string UltimateDay = "ultimate_day";

    /// <summary>
    /// Ultimate weekly subscription
    /// </summary>
    public const string UltimateWeek = "ultimate_week";

    /// <summary>
    /// Ultimate monthly subscription
    /// </summary>
    public const string UltimateMonth = "ultimate_month";

    /// <summary>
    /// Ultimate yearly subscription
    /// </summary>
    public const string UltimateYear = "ultimate_year";

    /// <summary>
    /// Gets all available membership tiers
    /// </summary>
    public static readonly string[] AllTiers = {
        Free,
        PremiumDay, PremiumWeek, PremiumMonth, PremiumYear,
        UltimateDay, UltimateWeek, UltimateMonth, UltimateYear
    };

    /// <summary>
    /// Gets all premium tier levels
    /// </summary>
    public static readonly string[] PremiumTiers = {
        PremiumDay, PremiumWeek, PremiumMonth, PremiumYear
    };

    /// <summary>
    /// Gets all ultimate tier levels
    /// </summary>
    public static readonly string[] UltimateTiers = {
        UltimateDay, UltimateWeek, UltimateMonth, UltimateYear
    };

    /// <summary>
    /// Checks if the tier represents a premium subscription
    /// </summary>
    /// <param name="tier">The membership tier to check</param>
    /// <returns>True if the tier is a premium subscription</returns>
    public static bool IsPremiumTier(string tier)
    {
        return tier.StartsWith("premium_");
    }

    /// <summary>
    /// Checks if the tier represents an ultimate subscription
    /// </summary>
    /// <param name="tier">The membership tier to check</param>
    /// <returns>True if the tier is an ultimate subscription</returns>
    public static bool IsUltimateTier(string tier)
    {
        return tier.StartsWith("ultimate_");
    }

    /// <summary>
    /// Checks if the tier represents any paid subscription
    /// </summary>
    /// <param name="tier">The membership tier to check</param>
    /// <returns>True if the tier is a paid subscription</returns>
    public static bool IsPaidTier(string tier)
    {
        return tier != Free;
    }

    /// <summary>
    /// Gets the subscription type (premium/ultimate/free) from the tier
    /// </summary>
    /// <param name="tier">The membership tier</param>
    /// <returns>The subscription type</returns>
    public static string GetSubscriptionType(string tier)
    {
        if (IsUltimateTier(tier)) return "ultimate";
        if (IsPremiumTier(tier)) return "premium";
        return "free";
    }

    /// <summary>
    /// Gets the plan duration (day/week/month/year) from the tier
    /// </summary>
    /// <param name="tier">The membership tier</param>
    /// <returns>The plan duration, or null for free tier</returns>
    public static string? GetPlanDuration(string tier)
    {
        if (tier == Free) return null;
        
        var parts = tier.Split('_');
        return parts.Length > 1 ? parts[1] : null;
    }
}

