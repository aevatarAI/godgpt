using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.FreeTrialCode;
using GodGPT.GAgents.FreeTrialCode.SEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.FreeTrialCode;

/// <summary>
/// Interface for Free Trial Code Factory GAgent - manages batch-based free trial code generation
/// Each instance manages one batch of codes identified by BatchId
/// </summary>
public interface IFreeTrialCodeFactoryGAgent : IGAgent
{
    /// <summary>
    /// Generate specified quantity of free trial reward codes
    /// </summary>
    Task<GenerateCodesResultDto> GenerateCodesAsync(GenerateCodesRequestDto request);

    /// <summary>
    /// Get current batch information
    /// </summary>
    [ReadOnly]
    Task<BatchInfoDto> GetBatchInfoAsync();

    /// <summary>
    /// Mark code as used
    /// </summary>
    Task<bool> MarkCodeAsUsedAsync(string code, string userId);

    /// <summary>
    /// Validate if code belongs to current batch
    /// </summary>
    [ReadOnly]
    Task<bool> ValidateCodeOwnershipAsync(string code);

    [ReadOnly]
    Task<bool> ValidateCodeAvailableAsync(string code);
}

[GAgent(nameof(FreeTrialCodeFactoryGAgent))]
public class FreeTrialCodeFactoryGAgent : GAgentBase<FreeTrialCodeFactoryState, FreeTrialCodeFactoryLogEvent>,
    IFreeTrialCodeFactoryGAgent
{
    private readonly ILogger<FreeTrialCodeFactoryGAgent> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;

    private const int MaxQuantity = 10000;

    public FreeTrialCodeFactoryGAgent(ILogger<FreeTrialCodeFactoryGAgent> logger,
        IOptionsMonitor<StripeOptions> stripeOptions, IOptionsMonitor<CreditsOptions> creditsOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
        _creditsOptions = creditsOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Free Trial Code Factory Management GAgent");
    }

    public async Task<GenerateCodesResultDto> GenerateCodesAsync(GenerateCodesRequestDto request)
    {
        if (!IsUserAuthorizedToGenerateCode(request.OperatorUserId.ToString()))
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Unauthorized attempt to generate codes by user {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Unauthorized attempt to generate code",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }
        
        await InitializeFactoryAsync(request);

        if (State.BatchId == null)
        {
            _logger.LogError(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory not initialized. OperatorUserId: {OperatorUserId}",
                request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Factory not initialized",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (State.Status != FreeTrialCodeFactoryStatus.Active)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Factory is not active. BatchId: {BatchId}, Status: {Status}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.Status, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = $"Factory is not active. Status: {State.Status}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (request.Quantity > MaxQuantity)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Requested quantity exceeds maximum. BatchId: {BatchId}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = $"Quantity cannot exceed batch max quantity: {MaxQuantity}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        if (State.TotalCodesGenerated + request.Quantity > MaxQuantity)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Would exceed batch capacity. BatchId: {BatchId}, Current: {CurrentGenerated}, Requested: {RequestedQuantity}, Max: {MaxQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, State.TotalCodesGenerated, request.Quantity, MaxQuantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message =
                    $"Would exceed batch capacity. Current: {State.TotalCodesGenerated}, Requested: {request.Quantity}, Max: {MaxQuantity}",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }

        try
        {
            var codes = await GenerateCodesInternalAsync(request.Quantity);

            if (codes.Count != request.Quantity)
            {
                _logger.LogWarning(
                    "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Generated code count mismatch. BatchId: {BatchId}, Expected: {ExpectedQuantity}, Actual: {ActualCount}, OperatorUserId: {OperatorUserId}",
                    State.BatchId, request.Quantity, codes.Count, request.OperatorUserId);
            }

            RaiseEvent(new GenerateCodesLogEvent
            {
                GeneratedCodes = codes.ToList(),
                Quantity = request.Quantity,
                Status = FreeTrialCodeFactoryStatus.Completed,
                CreationTime = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation(
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Successfully generated codes. BatchId: {BatchId}, Count: {Count}, OperatorUserId: {OperatorUserId}",
                State.BatchId, codes.Count, request.OperatorUserId);

            return new GenerateCodesResultDto
            {
                Success = true,
                Message = "Codes generated successfully",
                Codes = codes,
                GeneratedCount = codes.Count,
                ErrorCode = FreeTrialCodeError.None
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[FreeTrialCodeFactoryGAgent][GenerateCodesAsync] Error generating codes. BatchId: {BatchId}, RequestedQuantity: {RequestedQuantity}, OperatorUserId: {OperatorUserId}",
                State.BatchId, request.Quantity, request.OperatorUserId);
            return new GenerateCodesResultDto
            {
                Success = false,
                Message = "Internal error occurred while generating codes",
                ErrorCode = FreeTrialCodeError.InternalError
            };
        }
    }

    public async Task<bool> InitializeFactoryAsync(GenerateCodesRequestDto request)
    {
        if (State.BatchId != null)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][InitializeFactoryAsync] Factory already initialized. BatchId: {BatchId}, OperatorUserId: {OperatorUserId}", 
                State.BatchId, request.OperatorUserId);
            return false;
        }

        var stripeProduct = await GetStripeProductConfigAsync(request.ProductId);

        var batchConfig = new FreeTrialCodeBatchConfig
        {
            TrialDays = request.TrialDays,
            ProductId = stripeProduct.PriceId,
            PlanType = (PlanType)stripeProduct.PlanType,
            IsUltimate = stripeProduct.IsUltimate,
            Platform = PaymentPlatform.Stripe,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Description = request.Description,
        };

        RaiseEvent(new InitializeFactoryLogEvent
        {
            BatchId = request.BatchId,
            OperatorUserId = request.OperatorUserId,
            BatchConfig = batchConfig,
            CreationTime = DateTime.UtcNow,
            Status = FreeTrialCodeFactoryStatus.Active
        });

        await ConfirmEvents();

        _logger.LogInformation(
            "[FreeTrialCodeFactoryGAgent][InitializeFactoryAsync] Factory initialized successfully. BatchId: {BatchId}, OperatorUserId: {OperatorUserId}",
            request.BatchId, request.OperatorUserId);

        return true;
    }

    public Task<BatchInfoDto> GetBatchInfoAsync()
    {
        var batchInfo = new BatchInfoDto
        {
            BatchId = State.BatchId ?? 0,
            TotalGenerated = State.TotalCodesGenerated,
            UsedCount = State.UsedCount,
            CreationTime = State.CreationTime,
            LastGenerationTime = State.LastGenerationTime,
            Config = State.BatchConfig,
            Status = State.Status,
            GeneratedCodes = State.GeneratedCodes,
            UsedCodes = State.UsedCodes
        };

        return Task.FromResult(batchInfo);
    }

    public async Task<bool> MarkCodeAsUsedAsync(string code, string userId)
    {
        if (!await ValidateCodeOwnershipAsync(code))
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                code, State.BatchId, userId);
            return false;
        }

        if (State.UsedCodes.Contains(code))
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code already used. Code: {Code}, BatchId: {BatchId}, UserId: {UserId}", 
                code, State.BatchId, userId);
            return false;
        }

        RaiseEvent(new MarkCodeUsedLogEvent
        {
            Code = code,
            UserId = userId,
            UsedAt = DateTime.UtcNow
        });

        await ConfirmEvents();

        _logger.LogInformation(
            "[FreeTrialCodeFactoryGAgent][MarkCodeAsUsedAsync] Code marked as used successfully. Code: {Code}, UserId: {UserId}, BatchId: {BatchId}",
            code, userId, State.BatchId);

        return true;
    }

    public Task<bool> ValidateCodeOwnershipAsync(string code)
    {
        if (State.BatchId == null)
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(code)
            || State.GeneratedCodes.IsNullOrEmpty()
            || !InvitationCodeHelper.IsValidFreeTrialCodeFormat(code))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!InvitationCodeHelper.IsCodeFromBatch(code, State.BatchId ?? 0))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(State.GeneratedCodes.Contains(code));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "[FreeTrialCodeFactoryGAgent][ValidateCodeOwnershipAsync] Error validating code ownership. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> ValidateCodeAvailableAsync(string code)
    {
        if (!await ValidateCodeOwnershipAsync(code))
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not found in batch. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return false;
        }

        if (State.UsedCodes.Contains(code))
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code already used. Code: {Code}, BatchId: {BatchId}", 
                code, State.BatchId);
            return false;
        }

        var currentTime = DateTime.UtcNow;
        var startTime = State.BatchConfig.StartTime;
        var endTime = State.BatchConfig.EndTime;

        if (currentTime < startTime)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code not yet valid. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, Start: {StartTime}",
                code, State.BatchId, currentTime, startTime);
            return false;
        }

        if (currentTime > endTime)
        {
            _logger.LogWarning(
                "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code has expired. Code: {Code}, BatchId: {BatchId}, Current: {CurrentTime}, End: {EndTime}",
                code, State.BatchId, currentTime, endTime);
            return false;
        }

        _logger.LogDebug(
            "[FreeTrialCodeFactoryGAgent][ValidateCodeAvailableAsync] Code is available for use. Code: {Code}, BatchId: {BatchId}", 
            code, State.BatchId);
        return true;
    }

    private Task<HashSet<string>> GenerateCodesInternalAsync(int quantity)
    {
        var codes = new HashSet<string>();
        var usedCodes = new HashSet<string>(State.GeneratedCodes);

        var codeType = InvitationCodeType.FreeTrialReward;
        var unixTimestamp = State.BatchId ?? 0;

        for (int i = 0; i < quantity; i++)
        {
            string fullCode;
            do
            {
                fullCode = InvitationCodeHelper.GenerateOptimizedCode(codeType, unixTimestamp);
            } while (usedCodes.Contains(fullCode) || codes.Contains(fullCode));

            codes.Add(fullCode);
            usedCodes.Add(fullCode);
        }

        return Task.FromResult(codes);
    }

    private bool IsUserAuthorizedToGenerateCode(string operatorUserId)
    {
        var authorizedUsers = _creditsOptions.CurrentValue.OperatorUserId;
        return authorizedUsers.Contains(operatorUserId);
    }

    private Task<StripeProduct> GetStripeProductConfigAsync(string priceId)
    {
        var productConfig = _stripeOptions.CurrentValue.Products.FirstOrDefault(p => p.PriceId == priceId);
        if (productConfig == null)
        {
            _logger.LogError(
                "[FreeTrialCodeFactoryGAgent][GetStripeProductConfigAsync] Invalid priceId: {PriceId}. Product not found in configuration.",
                priceId);
            throw new ArgumentException($"Invalid priceId: {priceId}. Product not found in configuration.");
        }

        _logger.LogDebug(
            "[FreeTrialCodeFactoryGAgent][GetStripeProductConfigAsync] Found product with priceId: {PriceId}, planType: {PlanType}, amount: {Amount} {Currency}",
            productConfig.PriceId, productConfig.PlanType, productConfig.Amount, productConfig.Currency);

        return Task.FromResult(productConfig);
    }

    protected sealed override void GAgentTransitionState(FreeTrialCodeFactoryState state,
        StateLogEventBase<FreeTrialCodeFactoryLogEvent> @event)
    {
        switch (@event)
        {
            case InitializeFactoryLogEvent initEvent:
                state.BatchId = initEvent.BatchId;
                state.OperatorUserId = initEvent.OperatorUserId;
                state.BatchConfig = initEvent.BatchConfig;
                state.CreationTime = initEvent.CreationTime;
                state.Status = initEvent.Status;
                break;

            case GenerateCodesLogEvent generateEvent:
                state.GeneratedCodes = generateEvent.GeneratedCodes;
                state.TotalCodesGenerated = generateEvent.Quantity;
                state.LastGenerationTime = generateEvent.CreationTime;
                state.Status = generateEvent.Status;
                break;

            case MarkCodeUsedLogEvent usedEvent:
                state.UsedCount++;
                state.UsedCodes.Add(usedEvent.Code);
                break;

            case UpdateFactoryStatusLogEvent statusEvent:
                state.Status = statusEvent.Status;
                break;
        }
    }
}