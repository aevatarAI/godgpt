namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

/// <summary>
/// Twitter tweet type enumeration
/// </summary>
public enum TweetType
{
    /// <summary>
    /// Original tweet
    /// </summary>
    Original = 0,
    
    /// <summary>
    /// Reply tweet
    /// </summary>
    Reply = 1,
    
    /// <summary>
    /// Retweet
    /// </summary>
    Retweet = 2,
    
    /// <summary>
    /// Quote tweet
    /// </summary>
    Quote = 3
} 