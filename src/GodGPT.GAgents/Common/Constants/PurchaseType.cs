namespace Aevatar.Application.Grains.Common.Constants;

/// <summary>
/// Represents the type of purchase for analytics tracking
/// </summary>
[GenerateSerializer]
public enum PurchaseType
{
    /// <summary>
    /// Default value when purchase type is not specified
    /// </summary>
    [Id(0)] None = 0,
    
    /// <summary>
    /// Initial subscription purchase
    /// </summary>
    [Id(1)] Subscription = 1,
    
    /// <summary>
    /// Subscription renewal purchase
    /// </summary>
    [Id(2)] Renewal = 2,
    [Id(3)] Trial = 3
}
