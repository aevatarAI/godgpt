using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Shouldly;
using Xunit;

namespace GodGPT.GAgents.Tests.Common.Helpers
{
    public class SubscriptionHelperTests
    {
        [Fact]
        public void GetPlanTypeLogicalOrder_Should_Return_Correct_Logical_Order()
        {
            // Arrange & Act & Assert
            SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.None).ShouldBe(0);
            SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Day).ShouldBe(1);
            SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Week).ShouldBe(2);
            SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Month).ShouldBe(3);
            SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Year).ShouldBe(4);
        }

        [Fact]
        public void ComparePlanTypes_Should_Respect_Logical_Order_Not_Enum_Values()
        {
            // Test logical order: Day < Week < Month < Year
            // Note: Enum values are Day=1, Month=2, Year=3, Week=4 (historical compatibility)
            
            // Day < Week (logical order 1 < 2)
            SubscriptionHelper.ComparePlanTypes(PlanType.Day, PlanType.Week).ShouldBeLessThan(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Week, PlanType.Day).ShouldBeGreaterThan(0);
            
            // Week < Month (logical order 2 < 3)
            SubscriptionHelper.ComparePlanTypes(PlanType.Week, PlanType.Month).ShouldBeLessThan(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Month, PlanType.Week).ShouldBeGreaterThan(0);
            
            // Month < Year (logical order 3 < 4)
            SubscriptionHelper.ComparePlanTypes(PlanType.Month, PlanType.Year).ShouldBeLessThan(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Year, PlanType.Month).ShouldBeGreaterThan(0);
            
            // Day < Year (logical order 1 < 4)
            SubscriptionHelper.ComparePlanTypes(PlanType.Day, PlanType.Year).ShouldBeLessThan(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Year, PlanType.Day).ShouldBeGreaterThan(0);

            // Equal comparisons
            SubscriptionHelper.ComparePlanTypes(PlanType.Day, PlanType.Day).ShouldBe(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Week, PlanType.Week).ShouldBe(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Month, PlanType.Month).ShouldBe(0);
            SubscriptionHelper.ComparePlanTypes(PlanType.Year, PlanType.Year).ShouldBe(0);
        }

        [Fact]
        public void IsUpgrade_Should_Use_Logical_Order()
        {
            // Day -> Week should be upgrade
            SubscriptionHelper.IsUpgrade(PlanType.Day, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgrade(PlanType.Week, PlanType.Day).ShouldBeFalse();
            
            // Week -> Month should be upgrade  
            SubscriptionHelper.IsUpgrade(PlanType.Week, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgrade(PlanType.Month, PlanType.Week).ShouldBeFalse();
            
            // Month -> Year should be upgrade
            SubscriptionHelper.IsUpgrade(PlanType.Month, PlanType.Year).ShouldBeTrue();
            SubscriptionHelper.IsUpgrade(PlanType.Year, PlanType.Month).ShouldBeFalse();
            
            // Day -> Year should be upgrade
            SubscriptionHelper.IsUpgrade(PlanType.Day, PlanType.Year).ShouldBeTrue();
            SubscriptionHelper.IsUpgrade(PlanType.Year, PlanType.Day).ShouldBeFalse();
            
            // Same plan should not be upgrade
            SubscriptionHelper.IsUpgrade(PlanType.Day, PlanType.Day).ShouldBeFalse();
            SubscriptionHelper.IsUpgrade(PlanType.Week, PlanType.Week).ShouldBeFalse();
            SubscriptionHelper.IsUpgrade(PlanType.Month, PlanType.Month).ShouldBeFalse();
            SubscriptionHelper.IsUpgrade(PlanType.Year, PlanType.Year).ShouldBeFalse();
        }

        [Fact]
        public void IsUpgradeOrSameLevel_Should_Use_Logical_Order()
        {
            // Upgrades should return true
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Day, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Week, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Month, PlanType.Year).ShouldBeTrue();
            
            // Same level should return true
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Day, PlanType.Day).ShouldBeTrue();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Week, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Month, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Year, PlanType.Year).ShouldBeTrue();
            
            // Downgrades should return false
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Week, PlanType.Day).ShouldBeFalse();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Month, PlanType.Week).ShouldBeFalse();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Year, PlanType.Month).ShouldBeFalse();
            SubscriptionHelper.IsUpgradeOrSameLevel(PlanType.Year, PlanType.Day).ShouldBeFalse();
        }

        [Fact]
        public void IsUpgradePathValid_Should_Use_Logical_Order_For_Standard_Subscriptions()
        {
            // Standard to Standard upgrades based on logical order
            SubscriptionHelper.IsUpgradePathValid(PlanType.Day, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Day, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Day, PlanType.Year).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Week, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Week, PlanType.Year).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Month, PlanType.Year).ShouldBeTrue();
            
            // Standard to Standard renewals (same plan)
            SubscriptionHelper.IsUpgradePathValid(PlanType.Day, PlanType.Day).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Week, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Month, PlanType.Month).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Year, PlanType.Year).ShouldBeTrue();
            
            // Standard to Ultimate should always be valid
            SubscriptionHelper.IsUpgradePathValid(PlanType.Day, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Month, PlanType.Week).ShouldBeTrue();
            SubscriptionHelper.IsUpgradePathValid(PlanType.Year, PlanType.Week).ShouldBeTrue();
        }
    }
} 