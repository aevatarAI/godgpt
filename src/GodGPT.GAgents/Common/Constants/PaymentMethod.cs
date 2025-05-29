namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PaymentMethod
{
    [Id(0)] Card = 1,
    [Id(1)] GoogleWallet = 2,
    [Id(2)] PayPal = 3,
    [Id(3)] ApplePay = 4,
    [Id(4)] GooglePay = 5,
    [Id(5)] Link = 6,
    [Id(5)] Other = 7
}