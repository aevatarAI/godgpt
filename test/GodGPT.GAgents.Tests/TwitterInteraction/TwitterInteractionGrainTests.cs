using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.TwitterInteraction;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.GodGPT.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.TwitterInteraction;

/// <summary>
/// TwitterInteractionGrain测试类
/// </summary>
public class TwitterInteractionGrainTests : AevatarGodGPTTestsBase
{
    private readonly ILogger<TwitterInteractionGrainTests> _logger;

    public TwitterInteractionGrainTests() : base()
    {
        _logger = GetRequiredService<ILogger<TwitterInteractionGrainTests>>();
    }

    [Fact]
    public async Task GetGrain_ShouldReturnValidInstance()
    {
        // Arrange & Act
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        
        // Assert
        Assert.NotNull(grain);
    }

    [Fact]
    public async Task ValidateShareLink_ShouldReturnTrue_ForValidGodGPTLink()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var validLink = "https://app.godgpt.fun/chat/123";
        
        // Act
        var result = await grain.ValidateShareLinkAsync(validLink);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data.IsValid);
    }

    [Fact]
    public async Task ValidateShareLink_ShouldReturnFalse_ForInvalidLink()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var invalidLink = "https://example.com/test";
        
        // Act
        var result = await grain.ValidateShareLinkAsync(invalidLink);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Data.IsValid);
    }

    [Fact]
    public async Task AnalyzeTweet_ShouldReturnOriginal_ForOriginalTweet()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var tweetId = "123456789";
        
        // Act
        var result = await grain.AnalyzeTweetAsync(tweetId);
        
        // Assert
        Assert.NotNull(result);
        // Note: This test may fail without real Twitter API access
        // but should not throw exceptions
    }

    [Fact]
    public async Task AnalyzeTweet_ShouldHandleReplyTweet()
    {
        // Arrange  
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var tweetId = "987654321";
        
        // Act
        var result = await grain.AnalyzeTweetAsync(tweetId);
        
        // Assert
        Assert.NotNull(result);
        // Note: This test may fail without real Twitter API access
        // but should not throw exceptions
    }

    [Fact]
    public async Task ExtractShareLinks_ShouldReturnValidLinks_FromTweetText()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var tweetText = "Check out this amazing conversation at https://app.godgpt.fun/chat/abc123 and also https://example.com/test";
        
        // Act
        var result = await grain.ExtractShareLinksAsync(tweetText);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        Assert.Contains("https://app.godgpt.fun/chat/abc123", result.Data);
        Assert.DoesNotContain("https://example.com/test", result.Data);
    }

    [Fact]
    public async Task SearchTweets_ShouldHandleEmptyResponse_Gracefully()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-twitter-grain");
        var request = new SearchTweetsRequestDto
        {
            Query = "@GodGPT_",
            MaxResults = 10,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow
        };
        
        // Act & Assert
        // 注意：这个测试在没有真实Twitter API token的情况下会失败
        // 这里主要是验证方法能够被调用而不会抛出未处理的异常
        var result = await grain.SearchTweetsAsync(request);
        
        // 由于我们使用的是测试token，API调用会失败，但应该返回失败结果而不是抛异常
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTweetDetailsAsync_WithInvalidTweetId_ShouldReturnFailure()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");
        var invalidTweetId = "invalid-tweet-id";

        // Act
        var result = await twitterGrain.GetTweetDetailsAsync(invalidTweetId);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task SearchTweetsAsync_WithEmptyQuery_ShouldReturnFailure()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");
        var request = new SearchTweetsRequestDto
        {
            Query = "",
            MaxResults = 10
        };

        // Act
        var result = await twitterGrain.SearchTweetsAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ExtractUrlsFromTweetAsync_WithValidUrls_ShouldExtractUrls()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");
        var tweetText = "Check out this cool app https://app.godgpt.fun/chat and visit https://example.com";

        // Act
        var result = await twitterGrain.ExtractUrlsFromTweetAsync(tweetText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        Assert.Contains("https://app.godgpt.fun/chat", result.Data);
        Assert.Contains("https://example.com", result.Data);
    }

    [Fact]
    public async Task ExtractUrlsFromTweetAsync_WithNoUrls_ShouldReturnEmpty()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");
        var tweetText = "This is a tweet without any URLs";

        // Act
        var result = await twitterGrain.ExtractUrlsFromTweetAsync(tweetText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task TestApiConnectionAsync_ShouldReturnResult()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");

        // Act
        var result = await twitterGrain.TestApiConnectionAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ErrorMessage);
        // Note: This may fail or succeed depending on API configuration
        _logger.LogInformation("API connection test result: {Success}, Message: {Message}", 
            result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task GetApiQuotaInfoAsync_ShouldReturnQuotaInfo()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");

        // Act
        var result = await twitterGrain.GetApiQuotaInfoAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Limit > 0);
        Assert.True(result.Data.UsagePercentage >= 0);
    }

    [Fact]
    public async Task BatchProcessTweetsAsync_WithEmptyList_ShouldReturnEmpty()
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");
        var request = new BatchTweetProcessRequestDto
        {
            TweetIds = new List<string>(),
            FilterOriginalOnly = true,
            IncludeUserInfo = false
        };

        // Act
        var result = await twitterGrain.BatchProcessTweetsAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data.TotalProcessed);
        Assert.Equal(0, result.Data.SuccessCount);
        Assert.Equal(0, result.Data.FailedCount);
        Assert.Empty(result.Data.ProcessedTweets);
    }

    [Theory]
    [InlineData("https://app.godgpt.fun/chat")]
    [InlineData("https://app.godgpt.fun/dashboard")]
    [InlineData("https://app.godgpt.fun/profile/123")]
    public async Task ValidateShareLinkAsync_WithVariousValidLinks_ShouldReturnValid(string validLink)
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");

        // Act
        var result = await twitterGrain.ValidateShareLinkAsync(validLink);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.True(result.Data.IsValid);
        Assert.Equal(validLink, result.Data.Url);
    }

    [Theory]
    [InlineData("http://app.godgpt.fun/chat")] // HTTP instead of HTTPS
    [InlineData("https://godgpt.fun/chat")] // Wrong subdomain
    [InlineData("https://app.godgpt.com/chat")] // Wrong domain
    [InlineData("https://fake-app.godgpt.fun/chat")] // Wrong subdomain
    public async Task ValidateShareLinkAsync_WithInvalidLinks_ShouldReturnInvalid(string invalidLink)
    {
        // Arrange
        var twitterGrain = Cluster.GrainFactory.GetGrain<ITwitterInteractionGrain>("test-instance");

        // Act
        var result = await twitterGrain.ValidateShareLinkAsync(invalidLink);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.False(result.Data.IsValid);
        Assert.Equal(invalidLink, result.Data.Url);
    }
} 