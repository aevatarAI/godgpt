namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentType
{
    [Id(0)] Subscription = 0,
    [Id(1)] Credits = 1
}