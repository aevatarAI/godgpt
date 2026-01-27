using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Subscription utility methods for Ultimate mode and historical compatibility
/// </summary>
public static class SubscriptionHelper
{
    /// <summary>
    /// Checks if the subscription is Ultimate based on configuration flag
    /// </summary>
    public static bool IsUltimateSubscription(bool isUltimate)
    {
        return isUltimate;
    }

    /// <summary>
    /// Legacy method for backward compatibility - checks if plan type suggests Ultimate
    /// Note: This is deprecated and should be replaced with configuration-driven approach
    /// </summary>
    [Obsolete("Use IsUltimateSubscription(bool isUltimate) instead for configuration-driven Ultimate detection")]
    public static bool IsUltimateSubscription(PlanType planType)
    {
        // Legacy hardcoded logic - kept for backward compatibility during migration
        return false; // All plans are now standard by default, Ultimate is configuration-driven
    }

    /// <summary>
    /// Checks if the plan type is a standard subscription
    /// </summary>
    public static bool IsStandardSubscription(PlanType planType)
    {
        return planType is PlanType.Day 
            or PlanType.Week 
            or PlanType.Month 
            or PlanType.Year;
    }

    /// <summary>
    /// Normalizes plan type for business logic (treats legacy Day as Week)
    /// </summary>
    public static PlanType NormalizePlanType(PlanType planType)
    {
        // Treat legacy Day as Week for business logic
        return planType == PlanType.Day ? PlanType.Week : planType;
    }

    /// <summary>
    /// Gets display-friendly plan name with Ultimate suffix based on configuration
    /// </summary>
    public static string GetPlanDisplayName(PlanType planType, bool isUltimate = false)
    {
        var baseName = planType switch
        {
            PlanType.Day => "Weekly",  // Display legacy Day as Weekly
            PlanType.Week => "Weekly",
            PlanType.Month => "Monthly",
            PlanType.Year => "Annual",
            PlanType.None => "No Subscription",
            _ => "Unknown"
        };

        return isUltimate ? $"{baseName} Ultimate" : baseName;
    }

    /// <summary>
    /// Legacy method for backward compatibility
    /// </summary>
    [Obsolete("Use GetPlanDisplayName(PlanType planType, bool isUltimate) instead")]
    public static string GetPlanDisplayName(PlanType planType)
    {
        return GetPlanDisplayName(planType, false);
    }

    /// <summary>
    /// Calculates subscription end date based on plan type (configuration-driven Ultimate logic handled separately)
    /// </summary>
    public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
    {
        return planType switch
        {
            // Historical compatibility: Day treated as 7 days
            PlanType.Day => startDate.AddDays(1),
            PlanType.Week => startDate.AddDays(7),
            PlanType.Month => startDate.AddDays(30),
            PlanType.Year => startDate.AddDays(390),
            _ => throw new ArgumentException($"Invalid plan type: {planType}")
        };
    }

    /// <summary>
    /// Gets the number of days for a plan type (used for refund calculations)
    /// </summary>
    public static int GetDaysForPlanType(PlanType planType)
    {
        return planType switch
        {
            // Historical compatibility: Day treated as 7 days
            PlanType.Day => 1,
            PlanType.Week => 7,
            PlanType.Month => 30,
            PlanType.Year => 390,
            _ => throw new ArgumentException($"Invalid plan type: {planType}")
        };
    }

    /// <summary>
    /// Calculates daily average price for a plan
    /// </summary>
    public static decimal CalculateDailyAveragePrice(PlanType planType, decimal amount)
    {
        var days = GetDaysForPlanType(planType);
        return Math.Round(amount / days, 2, MidpointRounding.ToZero);
    }

    /// <summary>
    /// Validates if an upgrade path is allowed (updated for configuration-driven Ultimate)
    /// </summary>
    public static bool IsUpgradePathValid(PlanType fromPlan, PlanType toPlan)
    {
        // upgrades based on logical order: Day/Week -> Month/Year, Month -> Year
        var fromOrder = GetPlanTypeLogicalOrder(fromPlan);
        var toOrder = GetPlanTypeLogicalOrder(toPlan);
            
        // Allow upgrades (higher logical order) or same plan (renewal)
        return toOrder >= fromOrder;
    }

    /// <summary>
    /// Gets the logical order value for plan type comparison (Day=1, Week=2, Month=3, Year=4)
    /// This handles historical compatibility where enum values don't match logical order
    /// </summary>
    public static int GetPlanTypeLogicalOrder(PlanType planType)
    {
        return planType switch
        {
            PlanType.Day => 1,     // Logical order: 1st level
            PlanType.Week => 2,    // Logical order: 2nd level  
            PlanType.Month => 3,   // Logical order: 3rd level
            PlanType.Year => 4,    // Logical order: 4th level
            PlanType.None => 0,    // No subscription
            _ => 0
        };
    }

    /// <summary>
    /// Compares two plan types based on logical order rather than enum values
    /// Returns: -1 if plan1 < plan2, 0 if equal, 1 if plan1 > plan2
    /// </summary>
    public static int ComparePlanTypes(PlanType plan1, PlanType plan2)
    {
        var order1 = GetPlanTypeLogicalOrder(plan1);
        var order2 = GetPlanTypeLogicalOrder(plan2);
        return order1.CompareTo(order2);
    }

    /// <summary>
    /// Checks if target plan is an upgrade from current plan (logical order comparison)
    /// </summary>
    public static bool IsUpgrade(PlanType fromPlan, PlanType toPlan)
    {
        return ComparePlanTypes(toPlan, fromPlan) > 0;
    }

    /// <summary>
    /// Checks if target plan is same level or upgrade from current plan (logical order comparison)
    /// </summary>
    public static bool IsUpgradeOrSameLevel(PlanType fromPlan, PlanType toPlan)
    {
        return ComparePlanTypes(toPlan, fromPlan) >= 0;
    }

    public static string GetMembershipLevel(bool isUltimate)
    {
        return isUltimate ? MembershipLevel.Membership_Level_Ultimate : MembershipLevel.Membership_Level_Premium;
    }
} 