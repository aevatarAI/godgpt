using Orleans;

namespace Aevatar.Application.Grains.PaymentAnalytics.Dtos;

/// <summary>
/// Payment analytics operation result
/// </summary>
[GenerateSerializer]
public class PaymentAnalyticsResultDto
{
    [Id(0)]
    public bool IsSuccess { get; set; }
    
    [Id(1)]
    public string? ErrorMessage { get; set; }
    
    [Id(2)]
    public int StatusCode { get; set; }
}

/// <summary>
/// Payment analytics operation result with data
/// </summary>
/// <typeparam name="T">Data type</typeparam>
public class PaymentAnalyticsResultDto<T> : PaymentAnalyticsResultDto
{
    public T Data { get; set; } = default!;
}
