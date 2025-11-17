using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.UserBilling.Services;

/// <summary>
/// Apple payment service interface for handling Apple Pay and App Store specific payment operations
/// </summary>
public interface IApplePaymentService
{
    /// <summary>
    /// Get available Apple products
    /// </summary>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>List of Apple products</returns>
    Task<List<AppleProductDto>> GetProductsAsync();
}


public class ApplePaymentService : IApplePaymentService
{
    private readonly ILogger<ApplePaymentService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;

    public ApplePaymentService(
        ILogger<ApplePaymentService> logger,
        IHttpClientFactory httpClientFactory, IOptionsMonitor<ApplePayOptions> appleOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _appleOptions = appleOptions;
    }

    public async Task<List<AppleProductDto>> GetProductsAsync()
    {
        var products = _appleOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[ApplePaymentService][GetProductsAsync] No products configured in ApplePayOptions");
            return new List<AppleProductDto>();
        }

        _logger.LogDebug("[ApplePaymentService][GetProductsAsync] Found {Count} products in configuration",
            products.Count);
        var productDtos = new List<AppleProductDto>();
        foreach (var product in products)
        {
            var dailyAvgPrice = string.Empty;
            if (product.PlanType == (int)PlanType.Day)
            {
                dailyAvgPrice = product.Amount.ToString();
            } else if (product.PlanType == (int)PlanType.Week)
            {
                dailyAvgPrice = Math.Round(product.Amount / 7, 2, MidpointRounding.ToZero).ToString();
            }
            else if (product.PlanType == (int)PlanType.Month)
            {
                dailyAvgPrice = Math.Round(product.Amount / 30, 2, MidpointRounding.ToZero).ToString();
            }
            else if (product.PlanType == (int)PlanType.Year)
            {
                dailyAvgPrice = Math.Round(product.Amount / 390, 2, MidpointRounding.ToZero).ToString();
            }

            productDtos.Add(new AppleProductDto
            {
                PlanType = product.PlanType,
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Amount = product.Amount,
                DailyAvgPrice = dailyAvgPrice,
                Currency = product.Currency
            });
        }
        
        _logger.LogDebug("[ApplePaymentService][GetProductsAsync] Successfully retrieved {Count} products",
            productDtos.Count);
        return productDtos;
    }
}
