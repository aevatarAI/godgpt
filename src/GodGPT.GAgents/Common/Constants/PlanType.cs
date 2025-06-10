namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PlanType
{
    [Id(0)] Day = 1,
    [Id(1)] Month = 2,
    [Id(2)] Year = 3,
    [Id(3)] None = 0,
    [Id(4)] Week = 4
}