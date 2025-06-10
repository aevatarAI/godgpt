namespace Aevatar.Application.Grains.Common.Constants;

[GenerateSerializer]
public enum PlanType
{
    [Id(0)] Day = 1,            // Historical compatibility - treated as 7-day duration
    [Id(1)] Month = 2,          // Monthly subscription
    [Id(2)] Year = 3,           // Annual subscription
    [Id(3)] None = 0,           // No active subscription
    
    // New subscription types
    [Id(4)] Week = 4            // Weekly subscription (new standard plan)
}