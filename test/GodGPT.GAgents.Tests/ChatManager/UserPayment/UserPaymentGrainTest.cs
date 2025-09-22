using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.ChatManager.UserPayment;

public class UserPaymentGrainTest : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;

    public UserPaymentGrainTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task GetDiscountDetailsViaInvoiceAsyncTest()
    {
        var grainId = Guid.NewGuid();
        var userQuotaGrain = Cluster.GrainFactory.GetGrain<IUserPaymentGrain>(grainId);

        var discountsId = "in_1S3pdFQbIBhnP6iTpqSS4ZQf";
        var discountDetails = await userQuotaGrain.GetDiscountDetailsViaInvoiceAsync(discountsId);
        discountDetails.ShouldNotBeNull();
    }
}