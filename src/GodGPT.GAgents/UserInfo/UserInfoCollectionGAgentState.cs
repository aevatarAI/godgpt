using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserInfo;

[GenerateSerializer]
public class UserInfoCollectionGAgentState : StateBase
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public bool IsInitialized { get; set; }
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public DateTime LastUpdated { get; set; }
    
    // Name information (Step 2)
    [Id(4)] public string Gender { get; set; }
    [Id(5)] public string FirstName { get; set; }
    [Id(6)] public string LastName { get; set; }
    
    // Location information (Step 3)
    [Id(7)] public string Country { get; set; }
    [Id(8)] public string City { get; set; }
    
    // Birth date information (Step 4)
    [Id(9)] public int Day { get; set; }
    [Id(10)] public int Month { get; set; }
    [Id(11)] public int Year { get; set; }
    
    // Birth time information (Step 5) - Optional
    [Id(12)] public int? Hour { get; set; }
    [Id(13)] public int? Minute { get; set; }
    
    // Seeking interests (Step 6)
    [Id(14)] public List<string> SeekingInterests { get; set; } = new List<string>();
    
    // Source channels (Step 7)
    [Id(15)] public List<string> SourceChannels { get; set; } = new List<string>();
}
