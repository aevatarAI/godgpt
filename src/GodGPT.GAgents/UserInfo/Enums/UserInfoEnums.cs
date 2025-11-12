using System.ComponentModel;
using Orleans;

namespace Aevatar.Application.Grains.UserInfo.Enums;

/// <summary>
/// Seeking interest options with fixed codes
/// </summary>
[GenerateSerializer]
public enum SeekingInterestEnum
{
    [Description("Companionship")]
    Companionship = 0,
    
    [Description("Self-discovery")]
    SelfDiscovery = 1,
    
    [Description("Spiritual growth")]
    SpiritualGrowth = 2,
    
    [Description("Love & relationships")]
    LoveAndRelationships = 3,
    
    [Description("Daily fortune prediction")]
    DailyFortune = 4,
    
    [Description("Career guidance")]
    CareerGuidance = 5
}

/// <summary>
/// Source channel options with fixed codes
/// </summary>
[GenerateSerializer]
public enum SourceChannelEnum
{
    [Description("App Store / Play Store")]
    AppStorePlayStore = 0,
    
    [Description("Social media")]
    SocialMedia = 1,
    
    [Description("Search engine")]
    SearchEngine = 2,
    
    [Description("Friend referral")]
    FriendReferral = 3,
    
    [Description("Event / conference")]
    EventConference = 4,
    
    [Description("Advertisement")]
    Advertisement = 5,
    
    [Description("Other")]
    Other = 6
}
