using GodGPT.GAgents.Common.Observability;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GodGPT.GAgents.Tests.Common
{
    public class PaymentTelemetryMetricsTests
    {
        private readonly Mock<ILogger> _mockLogger;

        public PaymentTelemetryMetricsTests()
        {
            _mockLogger = new Mock<ILogger>();
        }

        [Fact]
        public void RecordPaymentSuccess_WithValidParameters_ShouldNotThrow()
        {
            // Arrange
            var paymentPlatform = "Apple";
            var purchaseType = "Subscription";
            var userId = "user123";
            var transactionId = "txn456";

            // Act & Assert
            var exception = Record.Exception(() =>
                PaymentTelemetryMetrics.RecordPaymentSuccess(
                    paymentPlatform,
                    purchaseType,
                    userId,
                    transactionId,
                    _mockLogger.Object));

            Assert.Null(exception);
        }

        [Fact]
        public void RecordPaymentSuccess_WithNullLogger_ShouldNotThrow()
        {
            // Arrange
            var paymentPlatform = "Google";
            var purchaseType = "OneTime";
            var userId = "user789";
            var transactionId = "txn012";

            // Act & Assert
            var exception = Record.Exception(() =>
                PaymentTelemetryMetrics.RecordPaymentSuccess(
                    paymentPlatform,
                    purchaseType,
                    userId,
                    transactionId,
                    null));

            Assert.Null(exception);
        }

        [Fact]
        public void RecordPaymentSuccess_WithEmptyUserId_ShouldNotThrow()
        {
            // Arrange
            var paymentPlatform = "Stripe";
            var purchaseType = "Premium";
            var userId = "";
            var transactionId = "txn345";

            // Act & Assert
            var exception = Record.Exception(() =>
                PaymentTelemetryMetrics.RecordPaymentSuccess(
                    paymentPlatform,
                    purchaseType,
                    userId,
                    transactionId,
                    _mockLogger.Object));

            Assert.Null(exception);
        }

        [Fact]
        public void RecordPaymentSuccess_WithNullUserId_ShouldNotThrow()
        {
            // Arrange
            var paymentPlatform = "Apple";
            var purchaseType = "Subscription";
            string userId = null;
            var transactionId = "txn678";

            // Act & Assert
            var exception = Record.Exception(() =>
                PaymentTelemetryMetrics.RecordPaymentSuccess(
                    paymentPlatform,
                    purchaseType,
                    userId,
                    transactionId,
                    _mockLogger.Object));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData("Apple", "Subscription")]
        [InlineData("Google", "OneTime")]
        [InlineData("Stripe", "Premium")]
        public void RecordPaymentSuccess_WithDifferentPlatformsAndTypes_ShouldNotThrow(
            string paymentPlatform, string purchaseType)
        {
            // Arrange
            var userId = "testUser";
            var transactionId = "testTxn";

            // Act & Assert
            var exception = Record.Exception(() =>
                PaymentTelemetryMetrics.RecordPaymentSuccess(
                    paymentPlatform,
                    purchaseType,
                    userId,
                    transactionId,
                    _mockLogger.Object));

            Assert.Null(exception);
        }

        [Fact]
        public void RecordPaymentSuccess_ShouldLogInformation_WhenLoggerProvided()
        {
            // Arrange
            var paymentPlatform = "Apple";
            var purchaseType = "Subscription";
            var userId = "user123";
            var transactionId = "txn456";

            // Act
            PaymentTelemetryMetrics.RecordPaymentSuccess(
                paymentPlatform,
                purchaseType,
                userId,
                transactionId,
                _mockLogger.Object);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("[PaymentTelemetry]")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
} 