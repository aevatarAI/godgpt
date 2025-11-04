using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Favourite prediction detail
/// </summary>
[GenerateSerializer]
public class FavouriteDetail
{
    [Id(0)] public DateOnly Date { get; set; }
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public DateTime FavouritedAt { get; set; }
}

/// <summary>
/// Fortune favourite state - manages user's favourite predictions
/// </summary>
[GenerateSerializer]
public class FortuneFavouriteState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    
    // Key: DateOnly (date), Value: FavouriteDetail
    [Id(1)] public Dictionary<DateOnly, FavouriteDetail> Favourites { get; set; } = new();
    
    [Id(2)] public DateTime LastUpdatedAt { get; set; }
}

