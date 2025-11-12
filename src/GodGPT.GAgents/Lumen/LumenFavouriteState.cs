using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

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
/// Lumen favourite state - manages user's favourite predictions
/// </summary>
[GenerateSerializer]
public class LumenFavouriteState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    
    // Key: PredictionId (Guid), Value: FavouriteDetail
    [Id(1)] public Dictionary<Guid, FavouriteDetail> Favourites { get; set; } = new();
    
    [Id(2)] public DateTime LastUpdatedAt { get; set; }
}

