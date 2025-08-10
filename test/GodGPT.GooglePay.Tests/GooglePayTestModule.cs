using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using GodGPT.Webhook.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Modularity;
using Moq;
using Moq.Contrib.HttpClient;
using System.Net;
using Google.Apis.AndroidPublisher.v3.Data;
using System.Text.Json;
using Aevatar.Application.Grains.UserBilling;
using System.IO;
using Autofac;
using Volo.Abp.Autofac;

namespace GodGPT.GooglePay.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class GooglePayTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Load configuration including Development settings
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();
        
        // Replace the existing configuration
        services.ReplaceConfiguration(configuration);
        
        // Configure GooglePayOptions from configuration
        services.Configure<GooglePayOptions>(configuration.GetSection("GooglePay"));
        
        // Register GooglePayOptions post processor for flat configuration support
        services.AddSingleton<IPostConfigureOptions<GooglePayOptions>, GooglePayOptionsPostProcessor>();
        
        // Create and register a mock for IGooglePayService
        var googlePayServiceMock = new Mock<IGooglePayService>();
        
        // Setup for Google Play purchases
        googlePayServiceMock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(r => r.PurchaseToken == "valid_subscription_token")))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = true, Message = "Subscription verified successfully", TransactionId = "trans_123", ProductId = "premium_monthly" });
        
        googlePayServiceMock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(r => r.PurchaseToken == "valid_product_token")))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = true, Message = "Product purchase verified successfully", TransactionId = "trans_456", ProductId = "premium_monthly" });
        
        googlePayServiceMock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(r => r.PurchaseToken == "not_found_token")))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "INVALID_PURCHASE_TOKEN", Message = "Purchase token not found" });
        
        googlePayServiceMock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(r => r.PurchaseToken == "api_error_token")))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "API_ERROR", Message = "Google Play API error" });
        
        // Default fallback for other tokens
        googlePayServiceMock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.IsAny<GooglePlayVerificationDto>()))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "INVALID_TOKEN", Message = "Invalid token" });
        
        // Set a default behavior for Google Pay web payments to avoid null returns
        googlePayServiceMock.Setup(x => x.VerifyGooglePayPaymentAsync(It.IsAny<GooglePayVerificationDto>()))
            .ReturnsAsync((GooglePayVerificationDto dto) => new PaymentVerificationResultDto 
            { 
                IsValid = false, 
                ErrorCode = "NO_MOCK_SETUP", 
                Message = "No mock setup for this test"
            });
        
        // Register both the mock and the service
        context.Services.AddSingleton(googlePayServiceMock);
        context.Services.AddSingleton<IGooglePayService>(sp => sp.GetRequiredService<Mock<IGooglePayService>>().Object);

        
        // --- Mock Grain for Webhook Test ---
        var userBillingGAgentMock = new Mock<IUserBillingGAgent>();
        userBillingGAgentMock
            .Setup(g => g.HandleGooglePlayNotificationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true); // Simulate successful processing

        services.AddSingleton(userBillingGAgentMock);
        services.AddSingleton<IUserBillingGAgent>(sp => sp.GetRequiredService<Mock<IUserBillingGAgent>>().Object);
        // --- End Mock Grain ---
        
        // Register webhook handler for testing
        services.AddTransient<GooglePayWebhookHandler>();
        
        // Register ILocalizationService for testing
        services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // Register other services needed for testing
        services.AddLogging();
    }
}
