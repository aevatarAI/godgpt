namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentType
{
    [Id(0)] Subscription = 0,
    [Id(1)] Credits = 1,
    [Id(2)] OneTime = 2,
    [Id(3)] Refund = 3,
    [Id(4)] Renewal = 4,
    [Id(5)] Cancellation = 5,
    [Id(6)] Unknown = 6
}