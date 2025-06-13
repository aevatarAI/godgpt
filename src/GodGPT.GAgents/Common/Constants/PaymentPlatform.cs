namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentPlatform
{
    [Id(0)] Stripe = 0,
    [Id(1)] AppStore = 1
}