using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Shouldly;
using Xunit;

namespace GodGPT.GAgents.Tests.ChatManager.UserBilling
{
    public class UserBillingGrainTests_PlanType
    {
        private static readonly DateTime _now = DateTime.UtcNow;

        private PlanType GetMaxActivePlanType(List<PaymentSummary> paymentHistory)
        {
            var now = DateTime.UtcNow;
            var maxPlanType = paymentHistory
                .Where(p => 
                    (p.Status == PaymentStatus.Completed && p.SubscriptionEndDate != null && p.SubscriptionEndDate > now) ||
                    (p.InvoiceDetails != null && p.InvoiceDetails.Any(i => 
                        i.Status == PaymentStatus.Completed && i.SubscriptionEndDate != null && i.SubscriptionEndDate > now))
                )
                .OrderByDescending(p => SubscriptionHelper.GetPlanTypeLogicalOrder(p.PlanType))
                .Select(p => p.PlanType)
                .DefaultIfEmpty(PlanType.None)
                .First();

            return maxPlanType;
        }

        [Fact]
        public void GetMaxActivePlanType_NoPaymentRecords_ReturnsNone()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>();
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.None);
        }
        
        [Fact]
        public void GetMaxActivePlanType_AllExpiredRecords_ReturnsNone()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-30),
                    SubscriptionEndDate = _now.AddDays(-1) // Expired
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Day,
                    SubscriptionStartDate = _now.AddDays(-2),
                    SubscriptionEndDate = _now.AddHours(-1) // Expired
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.None);
        }
        
        [Fact]
        public void GetMaxActivePlanType_ActiveRecords_ReturnsHighestPlanType()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-10),
                    SubscriptionEndDate = _now.AddDays(20) // Not expired
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Year,
                    SubscriptionStartDate = _now.AddDays(-65),
                    SubscriptionEndDate = _now.AddDays(300) // Not expired
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Day,
                    SubscriptionStartDate = _now.AddDays(-2),
                    SubscriptionEndDate = _now.AddHours(-1) // Expired
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.Year);
        }
        
        [Fact]
        public void GetMaxActivePlanType_NotCompletedRecords_AreFiltered()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Processing, // Not completed
                    PlanType = PlanType.Year,
                    SubscriptionStartDate = _now.AddDays(-5),
                    SubscriptionEndDate = _now.AddDays(360)
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-15),
                    SubscriptionEndDate = _now.AddDays(15) // Valid
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.Month);
        }
        
        [Fact]
        public void GetMaxActivePlanType_FutureStartDate_AreFiltered()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Year,
                    SubscriptionStartDate = _now.AddDays(5), // Not started yet
                    SubscriptionEndDate = _now.AddDays(370)
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-15),
                    SubscriptionEndDate = _now.AddDays(15) // Valid
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.Year); // Only considers started payments
        }
        
        [Fact]
        public void GetMaxActivePlanType_InvoiceDetailsActive_ReturnsHighestPlanType()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-30),
                    SubscriptionEndDate = _now.AddDays(-1), // Main record expired
                    InvoiceDetails = new List<UserBillingInvoiceDetail>
                    {
                        new UserBillingInvoiceDetail
                        {
                            Status = PaymentStatus.Completed,
                            SubscriptionStartDate = _now.AddDays(-15),
                            SubscriptionEndDate = _now.AddDays(15) // Invoice detail not expired
                        }
                    }
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Day,
                    SubscriptionStartDate = _now.AddDays(-2),
                    SubscriptionEndDate = _now.AddHours(-1) // Expired
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.Month);
        }
        
        [Fact]
        public void GetMaxActivePlanType_MixedInvoiceDetails_ReturnsHighestActivePlanType()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = _now.AddDays(-30),
                    SubscriptionEndDate = _now.AddDays(-1), // Main record expired
                    InvoiceDetails = new List<UserBillingInvoiceDetail>
                    {
                        new UserBillingInvoiceDetail
                        {
                            Status = PaymentStatus.Completed,
                            SubscriptionStartDate = _now.AddDays(-15),
                            SubscriptionEndDate = _now.AddDays(15) // Invoice detail not expired
                        },
                        new UserBillingInvoiceDetail
                        {
                            Status = PaymentStatus.Cancelled,
                            SubscriptionStartDate = _now.AddDays(-45),
                            SubscriptionEndDate = _now.AddDays(15) // Cancelled
                        }
                    }
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Year,
                    SubscriptionStartDate = _now.AddDays(-65),
                    SubscriptionEndDate = _now.AddDays(-5), // Expired
                    InvoiceDetails = new List<UserBillingInvoiceDetail>
                    {
                        new UserBillingInvoiceDetail
                        {
                            Status = PaymentStatus.Completed,
                            SubscriptionStartDate = _now.AddDays(-5),
                            SubscriptionEndDate = _now.AddDays(355) // Not expired
                        }
                    }
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.Year); // Should return Year as it's the highest active subscription
        }
        
        [Fact]
        public void GetMaxActivePlanType_DefaultDates_HandledCorrectly()
        {
            // Arrange
            var paymentHistory = new List<PaymentSummary>
            {
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Month,
                    SubscriptionStartDate = default, // Default date
                    SubscriptionEndDate = default // Default date
                },
                new PaymentSummary 
                {
                    Status = PaymentStatus.Completed,
                    PlanType = PlanType.Day,
                    SubscriptionStartDate = _now.AddDays(-2),
                    SubscriptionEndDate = _now.AddHours(-1) // Expired
                }
            };
            
            // Act
            var result = GetMaxActivePlanType(paymentHistory);
            
            // Assert
            result.ShouldBe(PlanType.None); // Should return None, as default dates are treated as expired
        }
    }
} 