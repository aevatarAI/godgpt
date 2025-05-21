namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentStatus
{
    [Id(0)] None = 0,
    [Id(1)] Pending = 1,
    [Id(2)] Processing = 2,
    [Id(3)] Completed = 3,
    [Id(4)] Failed = 4,
    [Id(5)] Refunded_In_Processing = 5,
    [Id(6)] Refunded = 6,
    [Id(7)] Cancelled_In_Processing = 7,
    [Id(8)] Cancelled = 8,
    [Id(9)] Disputed = 9
}