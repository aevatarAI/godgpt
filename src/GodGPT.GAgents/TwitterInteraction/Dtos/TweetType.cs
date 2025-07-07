namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

/// <summary>
/// Twitter推文类型枚举
/// </summary>
public enum TweetType
{
    /// <summary>
    /// 原创推文
    /// </summary>
    Original = 0,
    
    /// <summary>
    /// 回复推文
    /// </summary>
    Reply = 1,
    
    /// <summary>
    /// 转推
    /// </summary>
    Retweet = 2,
    
    /// <summary>
    /// 引用推文
    /// </summary>
    Quote = 3
} 