using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Subscription utility methods for Ultimate mode and historical compatibility
/// </summary>
public static class SubscriptionHelper
{
    /// <summary>
    /// Checks if the plan type is an Ultimate subscription
    /// </summary>
    public static bool IsUltimateSubscription(PlanType planType)
    {
        return planType is PlanType.WeekUltimate 
            or PlanType.MonthUltimate 
            or PlanType.YearUltimate;
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
    /// Gets display-friendly plan name
    /// </summary>
    public static string GetPlanDisplayName(PlanType planType)
    {
        return planType switch
        {
            PlanType.Day => "Weekly",  // Display legacy Day as Weekly
            PlanType.Week => "Weekly",
            PlanType.Month => "Monthly",
            PlanType.Year => "Annual",
            PlanType.WeekUltimate => "Weekly Ultimate",
            PlanType.MonthUltimate => "Monthly Ultimate",
            PlanType.YearUltimate => "Annual Ultimate",
            PlanType.None => "No Subscription",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Calculates subscription end date based on plan type
    /// Historical compatibility: Day treated as 7 days
    /// </summary>
    public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
    {
        return planType switch
        {
            // Historical compatibility: Day treated as 7 days
            PlanType.Day or PlanType.Week or PlanType.WeekUltimate => startDate.AddDays(7),
            PlanType.Month or PlanType.MonthUltimate => startDate.AddDays(30),
            PlanType.Year or PlanType.YearUltimate => startDate.AddDays(390),
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
            PlanType.Day or PlanType.Week or PlanType.WeekUltimate => 7,
            PlanType.Month or PlanType.MonthUltimate => 30,
            PlanType.Year or PlanType.YearUltimate => 390,
            _ => throw new ArgumentException($"Invalid plan type: {planType}")
        };
    }

    /// <summary>
    /// Calculates daily average price for a plan
    /// </summary>
    public static decimal CalculateDailyAveragePrice(PlanType planType, decimal amount)
    {
        var days = GetDaysForPlanType(planType);
        return Math.Round(amount / days, 2);
    }

    /// <summary>
    /// Validates if an upgrade path is allowed
    /// </summary>
    public static bool IsUpgradePathValid(PlanType fromPlan, PlanType toPlan)
    {
        // Standard subscriptions upgrade rules
        if (IsStandardSubscription(fromPlan))
        {
            // Can upgrade to any Ultimate
            if (IsUltimateSubscription(toPlan)) return true;
            
            // Standard upgrades: Day/Week -> Month/Year, Month -> Year
            return (fromPlan is PlanType.Day or PlanType.Week && toPlan is PlanType.Month or PlanType.Year)
                || (fromPlan == PlanType.Month && toPlan == PlanType.Year)
                || (fromPlan == toPlan); // Same plan (renewal)
        }

        // Ultimate subscriptions can be replaced by any Ultimate or coexist with standard
        if (IsUltimateSubscription(fromPlan))
        {
            return IsUltimateSubscription(toPlan) || IsStandardSubscription(toPlan);
        }

        return false;
    }
} 