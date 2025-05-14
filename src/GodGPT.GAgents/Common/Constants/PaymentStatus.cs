namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentStatus
{
    [Id(0)] None = 0,
    [Id(1)] Pending = 1,
    [Id(2)] Processing = 2,
    [Id(3)] Completed = 3,
    [Id(4)] Failed = 4,
    [Id(5)] Refunded = 5,
    [Id(6)] Cancelled = 6,
    [Id(7)] Disputed = 7
}