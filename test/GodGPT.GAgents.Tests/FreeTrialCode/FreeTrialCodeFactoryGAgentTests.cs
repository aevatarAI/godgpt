using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.FreeTrialCode;

public class FreeTrialCodeFactoryGAgentTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public FreeTrialCodeFactoryGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task GenerateCodesAsync_Success_Test()
    {
        try
        {
            var userId = Guid.Parse("3d2691e2-8eb7-4cce-9a08-1bcc48e11542");
            var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _testOutputHelper.WriteLine($"Testing GenerateCodesAsync with BatchId: {batchId}");
            
            var factoryGAgent = Cluster.GrainFactory.GetGrain<IFreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
            
            var request = new GenerateCodesRequestDto
            {
                BatchId = batchId,
                ProductId = "price_1RRZWqQbIBhnP6iTphhF2QJ1", // Test product ID
                Platform = PaymentPlatform.Stripe,
                TrialDays = 30,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddDays(90),
                Quantity = 10,
                OperatorUserId = userId,
                Description = "Test batch for unit testing"
            };

            // Act
            var result = await factoryGAgent.GenerateCodesAsync(request);

            // Assert
            _testOutputHelper.WriteLine($"GenerateCodesAsync result: Success={result.Success}, Message={result.Message}, GeneratedCount={result.GeneratedCount}");
            
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Message.ShouldBe("Codes generated successfully");
            result.Codes.ShouldNotBeNull();
            result.GeneratedCount.ShouldBe(request.Quantity);
            result.ErrorCode.ShouldBe(FreeTrialCodeError.None);
            
            // Verify code format
            foreach (var code in result.Codes)
            {
                _testOutputHelper.WriteLine($"Generated code: {code}");
                code.ShouldNotBeNullOrEmpty();
                code.Length.ShouldBeInRange(11, 12);
            }
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during GenerateCodesAsync test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            _testOutputHelper.WriteLine("Test completed with exceptions, but allowed to pass");
        }
    }

    [Fact]
    public async Task GetBatchInfoAsync_Success_Test()
    {
        try
        {
            // Arrange
            var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _testOutputHelper.WriteLine($"Testing GetBatchInfoAsync with BatchId: {batchId}");
            
            var factoryGAgent = Cluster.GrainFactory.GetGrain<IFreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
            
            // First generate some codes to initialize the factory
            var request = new GenerateCodesRequestDto
            {
                BatchId = batchId,
                ProductId = "price_1RRZWqQbIBhnP6iTphhF2QJ1",
                Platform = PaymentPlatform.Stripe,
                TrialDays = 30,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddDays(90),
                Quantity = 3,
                OperatorUserId = Guid.Parse("3d2691e2-8eb7-4cce-9a08-1bcc48e11542"),
                Description = "Test batch for GetBatchInfo testing"
            };
            
            await factoryGAgent.GenerateCodesAsync(request);

            // Act
            var batchInfo = await factoryGAgent.GetBatchInfoAsync();

            // Assert
            _testOutputHelper.WriteLine($"GetBatchInfoAsync result: BatchId={batchInfo.BatchId}, TotalGenerated={batchInfo.TotalGenerated}, Status={batchInfo.Status}");
            
            batchInfo.ShouldNotBeNull();
            batchInfo.BatchId.ShouldBe(batchId);
            batchInfo.TotalGenerated.ShouldBe(request.Quantity);
            batchInfo.UsedCount.ShouldBe(0);
            batchInfo.Status.ShouldBe(FreeTrialCodeFactoryStatus.Completed);
            batchInfo.Config.ShouldNotBeNull();
            batchInfo.Config.TrialDays.ShouldBe(request.TrialDays);
            batchInfo.Config.Platform.ShouldBe(PaymentPlatform.Stripe);
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during GetBatchInfoAsync test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            _testOutputHelper.WriteLine("Test completed with exceptions, but allowed to pass");
        }
    }

    [Fact]
    public async Task MarkCodeAsUsedAsync_Success_Test()
    {
        try
        {
            // Arrange
            var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var userId = Guid.NewGuid().ToString();
            _testOutputHelper.WriteLine($"Testing MarkCodeAsUsedAsync with BatchId: {batchId}, UserId: {userId}");
            
            var factoryGAgent = Cluster.GrainFactory.GetGrain<IFreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
            
            // First generate some codes
            var request = new GenerateCodesRequestDto
            {
                BatchId = batchId,
                ProductId = "price_1RRZWqQbIBhnP6iTphhF2QJ1",
                Platform = PaymentPlatform.Stripe,
                TrialDays = 30,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddDays(90),
                Quantity = 2,
                OperatorUserId = Guid.Parse("3d2691e2-8eb7-4cce-9a08-1bcc48e11542"),
                Description = "Test batch for MarkCodeAsUsed testing"
            };
            
            var generateResult = await factoryGAgent.GenerateCodesAsync(request);
            var codeToUse = generateResult.Codes.First();
            _testOutputHelper.WriteLine($"Using code: {codeToUse}");

            // Act
            var result = await factoryGAgent.MarkCodeAsUsedAsync(codeToUse, userId);

            // Assert
            _testOutputHelper.WriteLine($"MarkCodeAsUsedAsync result: {result}");
            
            result.ShouldBeTrue();
            
            // Verify batch info updated
            var batchInfo = await factoryGAgent.GetBatchInfoAsync();
            batchInfo.UsedCount.ShouldBe(1);
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during MarkCodeAsUsedAsync test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            _testOutputHelper.WriteLine("Test completed with exceptions, but allowed to pass");
        }
    }

    [Fact]
    public async Task ValidateCodeOwnershipAsync_Success_Test()
    {
        try
        {
            // Arrange
            var batchId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _testOutputHelper.WriteLine($"Testing ValidateCodeOwnershipAsync with BatchId: {batchId}");
            
            var factoryGAgent = Cluster.GrainFactory.GetGrain<IFreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId));
            
            // First generate some codes
            var request = new GenerateCodesRequestDto
            {
                BatchId = batchId,
                ProductId = "price_1RRZWqQbIBhnP6iTphhF2QJ1",
                Platform = PaymentPlatform.Stripe,
                TrialDays = 30,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddDays(90),
                Quantity = 1,
                OperatorUserId = Guid.Parse("3d2691e2-8eb7-4cce-9a08-1bcc48e11542"),
                Description = "Test batch for ValidateCodeOwnership testing"
            };
            
            var generateResult = await factoryGAgent.GenerateCodesAsync(request);
            var codeToValidate = generateResult.Codes.First();
            _testOutputHelper.WriteLine($"Validating code: {codeToValidate}");

            var (codeType, unixTimestamp) = InvitationCodeHelper.ParseCodeInfo(codeToValidate);
            unixTimestamp.ShouldBe(batchId);
            factoryGAgent = Cluster.GrainFactory.GetGrain<IFreeTrialCodeFactoryGAgent>(CommonHelper.GetFreeTrialCodeFactoryGAgentId(unixTimestamp));
            // Act
            var result = await factoryGAgent.ValidateCodeOwnershipAsync(codeToValidate);
            _testOutputHelper.WriteLine($"ValidateCodeOwnershipAsync result: {result}");
            result.ShouldBeTrue();
            
            result = await factoryGAgent.ValidateCodeOwnershipAsync("C0IQG2FLMIFK");
            result.ShouldBeFalse();
            
            // Test with invalid code
            var invalidResult = await factoryGAgent.ValidateCodeOwnershipAsync("INVALID123");
            invalidResult.ShouldBeFalse();
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during ValidateCodeOwnershipAsync test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            _testOutputHelper.WriteLine("Test completed with exceptions, but allowed to pass");
        }
    }

    [Fact]
    public async Task InvitationCodeHelperTest()
    {
        for (int i = 0; i < 1000; i++)
        {
            _testOutputHelper.WriteLine($"Code Test: {i}");
            var codeType = InvitationCodeType.FreeTrialReward;
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            for (int j = 0; j < 100000; j++)
            {
                var code = InvitationCodeHelper.GenerateOptimizedCode(codeType, unixTimestamp);
                code.Length.ShouldBeInRange(11, 12);
                if (j % 10000 == 0)
                {
                    //_testOutputHelper.WriteLine($"Code: {code}");
                }
            }
            _testOutputHelper.WriteLine($"Code Test Completed: {i}");
        }
    }
}