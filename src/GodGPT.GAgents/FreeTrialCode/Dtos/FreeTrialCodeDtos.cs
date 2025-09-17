using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.FreeTrialCode.Dtos;

[GenerateSerializer]
public class GenerateCodesRequestDto
{
    [Id(0)] public long BatchId { get; set; }
    [Id(1)] public string ProductId { get; set; }
    [Id(2)] public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Id(3)] public int TrialDays { get; set; } = 30;
    [Id(4)] public DateTime StartTime { get; set; }
    [Id(5)] public DateTime EndTime { get; set; }
    [Id(6)] public int Quantity { get; set; }
    [Id(7)] public Guid OperatorUserId { get; set; }
    [Id(8)] public string Description { get; set; }
}

[GenerateSerializer]
public class GenerateCodesResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; }
    [Id(2)] public HashSet<string> Codes { get; set; }
    [Id(3)] public int GeneratedCount { get; set; }
    [Id(4)] public FreeTrialCodeError ErrorCode { get; set; }
}

[GenerateSerializer]
public class BatchInfoDto
{
    [Id(0)] public long BatchId { get; set; }
    [Id(1)] public int TotalGenerated { get; set; }
    [Id(2)] public int UsedCount { get; set; }
    [Id(4)] public DateTime CreationTime { get; set; }
    [Id(5)] public DateTime LastGenerationTime { get; set; }
    [Id(6)] public FreeTrialCodeBatchConfig Config { get; set; }
    [Id(7)] public FreeTrialCodeFactoryStatus Status { get; set; }
    [Id(8)] public List<string> GeneratedCodes { get; set; } = new();
    [Id(9)] public List<string> UsedCodes { get; set; } = new();
}

[GenerateSerializer]
public class BatchStatsDto
{
    [Id(0)] public int TotalGenerated { get; set; }
    [Id(1)] public int UsedCount { get; set; }
    [Id(2)] public double UsageRate { get; set; }
    [Id(3)] public DateTime CreationTime { get; set; }
    [Id(4)] public DateTime? LastUsedTime { get; set; }
    [Id(5)] public bool IsExpired { get; set; }
}

[GenerateSerializer]
public class ValidateCodeResultDto
{
    [Id(0)] public bool IsValid { get; set; }
    [Id(1)] public string Message { get; set; }
    [Id(2)] public InvitationCodeType CodeType { get; set; }
    [Id(3)] public FreeTrialActivationDto ActivationInfo { get; set; }
}

[GenerateSerializer]
public class FreeTrialActivationDto
{
    [Id(0)] public int TrialDays { get; set; }
    [Id(1)] public PlanType PlanType { get; set; }
    [Id(2)] public bool IsUltimate { get; set; }
    [Id(3)] public DateTime StartDate { get; set; }
    [Id(4)] public DateTime EndDate { get; set; }
    [Id(5)] public string SubscriptionId { get; set; }
}

[GenerateSerializer]
public class FreeTrialCodeInitDto
{
    [Id(0)] public string BatchId { get; set; }
    [Id(1)] public int TrialDays { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public bool IsUltimate { get; set; }
    [Id(4)] public DateTime StartDate { get; set; }
    [Id(5)] public DateTime EndDate { get; set; }
}

[GenerateSerializer]
public class FreeTrialCodeInfoDto
{
    [Id(0)] public string BatchId { get; set; }
    [Id(1)] public int TrialDays { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public bool IsUltimate { get; set; }
    [Id(4)] public string InviteeId { get; set; }
    [Id(5)] public DateTime? UsedAt { get; set; }
}

[GenerateSerializer]
public class FreeTrialInfoDto
{
    [Id(1)] public string FreeTrialCode { get; set; }
    [Id(2)] public int TrialDays { get; set; }
    [Id(3)] public PlanType PlanType { get; set; }
    [Id(4)] public bool IsUltimate { get; set; }
    [Id(5)] public string TransactionId { get; set; }
}

[GenerateSerializer]
public class CreateFreeTrialDto
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public int TrialDays { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public bool IsUltimate { get; set; }
    [Id(4)] public string FreeTrialCode { get; set; }
}

[GenerateSerializer]
public class CreateSubscriptionResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; }
    [Id(2)] public string SubscriptionId { get; set; }
    [Id(3)] public DateTime StartDate { get; set; }
    [Id(4)] public DateTime EndDate { get; set; }
}

public enum FreeTrialCodeError
{
    None = 0,
    CodeNotFound = 4001,
    CodeAlreadyUsed = 4002,
    CodeExpired = 4003,
    UserAlreadyHasTrial = 4004,
    UserHasActiveSubscription = 4005,
    InvalidCodeType = 4006,
    InternalError = 5000
}

[GenerateSerializer]
public class FreeTrialCodeBatchConfig
{
    [Id(0)] public int TrialDays { get; set; }
    [Id(1)] public string ProductId { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public bool IsUltimate { get; set; }
    [Id(4)] public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Id(5)] public DateTime StartTime { get; set; }
    [Id(6)] public DateTime EndTime { get; set; }
    [Id(7)] public string Description { get; set; }
}

public enum FreeTrialCodeFactoryStatus
{
    Pending = 0,
    Active = 1,
    Completed = 2
}
