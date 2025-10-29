using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Apple payment service implementation
/// Handles Apple Pay and App Store specific payment operations
/// </summary>
public class ApplePaymentService : IApplePaymentService
{
    private readonly ILogger<ApplePaymentService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ApplePaymentService(
        ILogger<ApplePaymentService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<AppleProductDto>> GetProductsAsync(ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][GetProductsAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto dto, ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][CreateAppStoreSubscriptionAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<PaymentVerificationResultDto> ProcessAppStoreNotificationAsync(Guid userId, string jsonPayload, ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][ProcessAppStoreNotificationAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<AppStoreJWSTransactionDecodedPayload> GetAppStoreTransactionInfoAsync(string transactionId, string environment, ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][GetAppStoreTransactionInfoAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<PaymentVerificationResultDto> VerifyAppStoreReceiptAsync(string receiptData, ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][VerifyAppStoreReceiptAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<bool> ProcessSubscriptionRenewalAsync(Guid userId, AppStoreJWSTransactionDecodedPayload transactionInfo, ApplePayOptions options)
    {
        _logger.LogDebug("[ApplePaymentService][ProcessSubscriptionRenewalAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }
}
