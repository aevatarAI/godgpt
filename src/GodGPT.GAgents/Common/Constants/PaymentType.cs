namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentType
{
    [Id(0)] Subscription = 0,
    [Id(1)] Credits = 1,
    [Id(2)] OneTime = 2,
    [Id(3)] Refund = 3
}