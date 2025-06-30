using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter测试Grain实现
/// 提供测试环境控制功能，包括时间模拟、数据注入、任务触发等
/// </summary>
public class TwitterTestingGrain : Grain, ITwitterTestingGrain
{
    private readonly ILogger<TwitterTestingGrain> _logger;
    
    // 依赖的其他Grain
    private ITweetMonitorGrain? _tweetMonitorGrain;
    private ITwitterRewardGrain? _twitterRewardGrain;
    private ITwitterSystemManagerGrain? _systemManagerGrain;
    private ITwitterRecoveryGrain? _recoveryGrain;
    
    // 测试状态
    private bool _isTestModeActive = false;
    private int _testTimeOffsetHours = 0;
    private readonly List<TweetRecord> _testTweets = new();
    private readonly List<UserInfoDto> _testUsers = new();
    private readonly List<TestExecutionRecordDto> _testExecutionHistory = new();
    private readonly Dictionary<string, object> _testMetrics = new();
    
    public TwitterTestingGrain(ILogger<TwitterTestingGrain> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public override Task OnActivateAsync()
    {
        _logger.LogInformation($"TwitterTestingGrain activated: {this.GetPrimaryKeyString()}");
        
        // Initialize dependent grains
        _tweetMonitorGrain = GrainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
        _twitterRewardGrain = GrainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
        _systemManagerGrain = GrainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        _recoveryGrain = GrainFactory.GetGrain<ITwitterRecoveryGrain>("TwitterRecovery");
        
        return base.OnActivateAsync();
    }
    
    #region 时间控制测试
    
    public async Task<bool> SetTestTimeOffsetAsync(int offsetHours)
    {
        _logger.LogInformation($"Setting test time offset to {offsetHours} hours");
        
        try
        {
            _testTimeOffsetHours = offsetHours;
            await RecordTestExecution("SetTestTimeOffset", $"Time offset set to {offsetHours} hours", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error setting test time offset: {ex.Message}");
            await RecordTestExecution("SetTestTimeOffset", ex.Message, false);
            return false;
        }
    }
    
    public async Task<long> GetCurrentTestTimestampAsync()
    {
        try
        {
            var currentTime = DateTime.UtcNow.AddHours(_testTimeOffsetHours);
            var timestamp = ((DateTimeOffset)currentTime).ToUnixTimeSeconds();
            _logger.LogInformation($"Current test timestamp: {timestamp} (offset: {_testTimeOffsetHours}h)");
            return timestamp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting current test timestamp: {ex.Message}");
            return ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        }
    }
    
    public async Task<bool> SimulateTimePassageAsync(int minutes)
    {
        _logger.LogInformation($"Simulating time passage of {minutes} minutes");
        
        try
        {
            // Add the minutes to the current offset
            var additionalHours = minutes / 60.0;
            _testTimeOffsetHours += (int)Math.Round(additionalHours);
            
            await RecordTestExecution("SimulateTimePassage", $"Time advanced by {minutes} minutes", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error simulating time passage: {ex.Message}");
            await RecordTestExecution("SimulateTimePassage", ex.Message, false);
            return false;
        }
    }
    
    public async Task<bool> ResetTimeOffsetAsync()
    {
        _logger.LogInformation("Resetting test time offset to zero");
        
        try
        {
            _testTimeOffsetHours = 0;
            await RecordTestExecution("ResetTimeOffset", "Time offset reset to zero", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error resetting time offset: {ex.Message}");
            await RecordTestExecution("ResetTimeOffset", ex.Message, false);
            return false;
        }
    }
    
    #endregion
    
    #region 数据模拟
    
    public async Task<bool> InjectTestTweetDataAsync(List<TweetRecord> testTweets)
    {
        _logger.LogInformation($"Injecting {testTweets.Count} test tweets");
        
        try
        {
            // Clear existing test tweets
            _testTweets.Clear();
            
            // Add new test tweets
            foreach (var tweet in testTweets)
            {
                // Apply test time offset if needed
                if (_testTimeOffsetHours != 0)
                {
                    tweet.CreatedAt = tweet.CreatedAt.AddHours(_testTimeOffsetHours);
                    tweet.CreatedAtUtc = ((DateTimeOffset)tweet.CreatedAt).ToUnixTimeSeconds();
                }
                
                _testTweets.Add(tweet);
            }
            
            await RecordTestExecution("InjectTestTweetData", $"Injected {testTweets.Count} test tweets", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error injecting test tweet data: {ex.Message}");
            await RecordTestExecution("InjectTestTweetData", ex.Message, false);
            return false;
        }
    }
    
    public async Task<List<TweetRecord>> GenerateMockTweetDataAsync(int count, TimeRangeDto timeRange, TweetType tweetType = TweetType.Original)
    {
        _logger.LogInformation($"Generating {count} mock tweets of type {tweetType}");
        
        try
        {
            var mockTweets = new List<TweetRecord>();
            var random = new Random();
            
            for (int i = 0; i < count; i++)
            {
                // Generate random time within the range
                var randomSeconds = random.NextInt64(timeRange.StartTimeUtc, timeRange.EndTimeUtc);
                var tweetTime = DateTimeOffset.FromUnixTimeSeconds(randomSeconds).DateTime;
                
                var mockTweet = new TweetRecord
                {
                    TweetId = $"test_tweet_{Guid.NewGuid()}",
                    AuthorId = $"test_user_{random.Next(1, 100)}",
                    AuthorHandle = $"@testuser{random.Next(1, 100)}",
                    AuthorName = $"Test User {random.Next(1, 100)}",
                    CreatedAt = tweetTime,
                    CreatedAtUtc = randomSeconds,
                    Text = $"Test tweet content {i + 1} @GodGPT_ #test",
                    Type = tweetType,
                    ViewCount = random.Next(10, 10000),
                    FollowerCount = random.Next(5, 5000),
                    HasValidShareLink = random.NextDouble() > 0.7, // 30% have share links
                    ShareLinkUrl = random.NextDouble() > 0.7 ? $"https://app.godgpt.fun/share/{Guid.NewGuid()}" : string.Empty,
                    IsProcessed = false,
                    FetchedAt = DateTime.UtcNow
                };
                
                mockTweets.Add(mockTweet);
            }
            
            await RecordTestExecution("GenerateMockTweetData", $"Generated {count} mock tweets", true);
            return mockTweets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating mock tweet data: {ex.Message}");
            await RecordTestExecution("GenerateMockTweetData", ex.Message, false);
            return new List<TweetRecord>();
        }
    }
    
    public async Task<bool> InjectTestUserDataAsync(List<UserInfoDto> testUsers)
    {
        _logger.LogInformation($"Injecting {testUsers.Count} test users");
        
        try
        {
            _testUsers.Clear();
            _testUsers.AddRange(testUsers);
            
            await RecordTestExecution("InjectTestUserData", $"Injected {testUsers.Count} test users", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error injecting test user data: {ex.Message}");
            await RecordTestExecution("InjectTestUserData", ex.Message, false);
            return false;
        }
    }
    
    public async Task<bool> ClearAllTestDataAsync()
    {
        _logger.LogInformation("Clearing all test data");
        
        try
        {
            _testTweets.Clear();
            _testUsers.Clear();
            _testMetrics.Clear();
            
            await RecordTestExecution("ClearAllTestData", "All test data cleared", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error clearing test data: {ex.Message}");
            await RecordTestExecution("ClearAllTestData", ex.Message, false);
            return false;
        }
    }
    
    public async Task<TestDataSummaryDto> GetTestDataSummaryAsync()
    {
        _logger.LogInformation("Getting test data summary");
        
        try
        {
            var summary = new TestDataSummaryDto
            {
                TotalTestTweets = _testTweets.Count,
                TestUsers = _testUsers.Count,
                CurrentTestTimeOffset = _testTimeOffsetHours,
                IsTestModeActive = _isTestModeActive,
                LastDataInjection = _testTweets.Count > 0 ? _testTweets.Max(t => t.FetchedAt) : DateTime.MinValue,
                LastDataInjectionTimestamp = _testTweets.Count > 0 ? ((DateTimeOffset)_testTweets.Max(t => t.FetchedAt)).ToUnixTimeSeconds() : 0
            };
            
            // Group tweets by type
            summary.TweetsByType = _testTweets.GroupBy(t => t.Type.ToString())
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Group tweets by hour
            summary.TweetsByTimeRange = _testTweets.GroupBy(t => t.CreatedAt.ToString("yyyy-MM-dd HH"))
                .ToDictionary(g => g.Key, g => g.Count());
            
            await RecordTestExecution("GetTestDataSummary", "Test data summary generated", true);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting test data summary: {ex.Message}");
            await RecordTestExecution("GetTestDataSummary", ex.Message, false);
            return new TestDataSummaryDto();
        }
    }
    
    #endregion
    
    #region 任务触发测试
    
    public async Task<PullTweetResultDto> TriggerPullTaskAsync(bool useTestTime = true)
    {
        _logger.LogInformation($"Triggering pull task (useTestTime: {useTestTime})");
        
        try
        {
            if (_tweetMonitorGrain == null)
                throw new InvalidOperationException("TweetMonitorGrain not initialized");
            
            PullTweetResultDto result;
            
            if (useTestTime && _testTimeOffsetHours != 0)
            {
                // Calculate test time range
                var currentTestTime = DateTime.UtcNow.AddHours(_testTimeOffsetHours);
                var endTimestamp = ((DateTimeOffset)currentTestTime).ToUnixTimeSeconds();
                var startTimestamp = endTimestamp - (30 * 60); // 30 minutes before
                
                result = await _tweetMonitorGrain.PullTweetsByPeriodAsync(startTimestamp, endTimestamp);
            }
            else
            {
                // Use current time
                var currentTime = DateTime.UtcNow;
                var endTimestamp = ((DateTimeOffset)currentTime).ToUnixTimeSeconds();
                var startTimestamp = endTimestamp - (30 * 60);
                
                result = await _tweetMonitorGrain.PullTweetsByPeriodAsync(startTimestamp, endTimestamp);
            }
            
            await RecordTestExecution("TriggerPullTask", $"Pull task completed: {result.NewTweets} new tweets", result.Success);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error triggering pull task: {ex.Message}");
            await RecordTestExecution("TriggerPullTask", ex.Message, false);
            return new PullTweetResultDto { Success = false, ErrorMessage = ex.Message };
        }
    }
    
    public async Task<RewardCalculationResultDto> TriggerRewardTaskAsync(bool useTestTime = true)
    {
        _logger.LogInformation($"Triggering reward task (useTestTime: {useTestTime})");
        
        try
        {
            if (_twitterRewardGrain == null)
                throw new InvalidOperationException("TwitterRewardGrain not initialized");
            
            RewardCalculationResultDto result;
            
            if (useTestTime && _testTimeOffsetHours != 0)
            {
                // Calculate test time range (72-48 hours ago pattern)
                var currentTestTime = DateTime.UtcNow.AddHours(_testTimeOffsetHours);
                var endTimestamp = ((DateTimeOffset)currentTestTime.AddHours(-48)).ToUnixTimeSeconds();
                var startTimestamp = ((DateTimeOffset)currentTestTime.AddHours(-72)).ToUnixTimeSeconds();
                
                result = await _twitterRewardGrain.CalculateRewardsByPeriodAsync(startTimestamp, endTimestamp);
            }
            else
            {
                result = await _twitterRewardGrain.CalculateRewardsAsync();
            }
            
            await RecordTestExecution("TriggerRewardTask", $"Reward task completed: {result.TotalCreditsAwarded} credits awarded", result.IsSuccess);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error triggering reward task: {ex.Message}");
            await RecordTestExecution("TriggerRewardTask", ex.Message, false);
            return new RewardCalculationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }
    
    public async Task<TestProcessingResultDto> TriggerRangeProcessingAsync(TimeRangeDto timeRange, bool includePull = true, bool includeReward = true)
    {
        _logger.LogInformation($"Triggering range processing from {timeRange.StartTimeUtc} to {timeRange.EndTimeUtc}");
        
        var processingStart = DateTime.UtcNow;
        var result = new TestProcessingResultDto
        {
            ProcessedRange = timeRange,
            ProcessingStartTime = processingStart
        };
        
        try
        {
            if (includePull && _tweetMonitorGrain != null)
            {
                result.PullResult = await _tweetMonitorGrain.PullTweetsByPeriodAsync(timeRange.StartTimeUtc, timeRange.EndTimeUtc);
            }
            
            if (includeReward && _twitterRewardGrain != null)
            {
                result.RewardResult = await _twitterRewardGrain.CalculateRewardsByPeriodAsync(timeRange.StartTimeUtc, timeRange.EndTimeUtc);
            }
            
            result.Success = (result.PullResult?.Success ?? true) && (result.RewardResult?.IsSuccess ?? true);
            
            if (!result.Success)
            {
                var errors = new List<string>();
                if (result.PullResult != null && !result.PullResult.Success)
                    errors.Add($"Pull error: {result.PullResult.ErrorMessage}");
                if (result.RewardResult != null && !result.RewardResult.IsSuccess)
                    errors.Add($"Reward error: {result.RewardResult.ErrorMessage}");
                result.ErrorMessage = string.Join("; ", errors);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error triggering range processing: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            result.ProcessingEndTime = DateTime.UtcNow;
            result.ProcessingDuration = result.ProcessingEndTime - result.ProcessingStartTime;
            
            await RecordTestExecution("TriggerRangeProcessing", 
                result.Success ? "Range processing completed successfully" : result.ErrorMessage, 
                result.Success);
        }
        
        return result;
    }
    
    #endregion
    
    #region 状态控制
    
    public async Task<bool> ResetAllTaskStatesAsync()
    {
        _logger.LogInformation("Resetting all task states");
        
        try
        {
            // Reset system manager state
            if (_systemManagerGrain != null)
            {
                await _systemManagerGrain.ResetExecutionHistoryAsync();
            }
            
            // Reset reward grain state  
            if (_twitterRewardGrain != null)
            {
                await _twitterRewardGrain.ResetExecutionHistoryAsync();
            }
            
            // Reset monitor grain state
            if (_tweetMonitorGrain != null)
            {
                await _tweetMonitorGrain.ResetExecutionHistoryAsync();
            }
            
            await RecordTestExecution("ResetAllTaskStates", "All task states reset successfully", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error resetting task states: {ex.Message}");
            await RecordTestExecution("ResetAllTaskStates", ex.Message, false);
            return false;
        }
    }
    
    public async Task<bool> ResetExecutionHistoryAsync()
    {
        _logger.LogInformation("Resetting test execution history");
        
        try
        {
            _testExecutionHistory.Clear();
            _testMetrics.Clear();
            
            await RecordTestExecution("ResetExecutionHistory", "Test execution history reset", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error resetting execution history: {ex.Message}");
            await RecordTestExecution("ResetExecutionHistory", ex.Message, false);
            return false;
        }
    }
    
    public async Task<bool> SetTestModeAsync(bool enabled)
    {
        _logger.LogInformation($"Setting test mode to: {enabled}");
        
        try
        {
            _isTestModeActive = enabled;
            await RecordTestExecution("SetTestMode", $"Test mode set to {enabled}", true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error setting test mode: {ex.Message}");
            await RecordTestExecution("SetTestMode", ex.Message, false);
            return false;
        }
    }
    
    public async Task<bool> IsTestModeActiveAsync()
    {
        return await Task.FromResult(_isTestModeActive);
    }
    
    #endregion
    
    #region 场景测试
    
    public async Task<TestScenarioResultDto> ExecuteTestScenarioAsync(TestScenarioDto scenario)
    {
        _logger.LogInformation($"Executing test scenario: {scenario.ScenarioName}");
        
        var scenarioStart = DateTime.UtcNow;
        var result = new TestScenarioResultDto
        {
            ScenarioId = scenario.ScenarioId,
            ScenarioName = scenario.ScenarioName,
            ExecutionStartTime = scenarioStart,
            StepResults = new List<TestStepResultDto>()
        };
        
        try
        {
            foreach (var step in scenario.Steps)
            {
                var stepResult = await ExecuteTestStep(step);
                result.StepResults.Add(stepResult);
                
                if (!stepResult.Success && scenario.StopOnFirstFailure)
                {
                    result.ErrorMessage = $"Step '{step.StepName}' failed: {stepResult.ErrorMessage}";
                    break;
                }
            }
            
            result.Success = result.StepResults.All(s => s.Success);
            result.ExecutionEndTime = DateTime.UtcNow;
            result.TotalDuration = result.ExecutionEndTime - result.ExecutionStartTime;
            
            await RecordTestExecution("ExecuteTestScenario", 
                $"Scenario '{scenario.ScenarioName}' executed: {result.StepResults.Count(s => s.Success)}/{result.StepResults.Count} steps successful", 
                result.Success);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error executing test scenario: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ExecutionEndTime = DateTime.UtcNow;
            result.TotalDuration = result.ExecutionEndTime - result.ExecutionStartTime;
            
            await RecordTestExecution("ExecuteTestScenario", ex.Message, false);
            return result;
        }
    }
    
    public async Task<StressTestResultDto> ExecuteStressTestAsync(StressTestConfigDto config)
    {
        _logger.LogInformation($"Executing stress test: {config.TestName} with {config.ConcurrentUsers} concurrent users");
        
        var testStart = DateTime.UtcNow;
        var result = new StressTestResultDto
        {
            TestName = config.TestName,
            ConcurrentUsers = config.ConcurrentUsers,
            TestDurationMinutes = config.TestDurationMinutes,
            StartTime = testStart,
            Metrics = new Dictionary<string, object>()
        };
        
        try
        {
            var tasks = new List<Task>();
            var successCount = 0;
            var errorCount = 0;
            var responseTimes = new List<long>();
            
            // Execute concurrent load
            for (int i = 0; i < config.ConcurrentUsers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var userTestStart = DateTime.UtcNow;
                    try
                    {
                        await SimulateUserBehavior(config);
                        var responseTime = (DateTime.UtcNow - userTestStart).TotalMilliseconds;
                        lock (responseTimes)
                        {
                            responseTimes.Add((long)responseTime);
                            successCount++;
                        }
                    }
                    catch
                    {
                        lock (responseTimes)
                        {
                            errorCount++;
                        }
                    }
                }));
            }
            
            await Task.WhenAll(tasks);
            
            result.EndTime = DateTime.UtcNow;
            result.ActualDuration = result.EndTime - result.StartTime;
            result.TotalRequests = config.ConcurrentUsers;
            result.SuccessfulRequests = successCount;
            result.FailedRequests = errorCount;
            result.AverageResponseTime = responseTimes.Count > 0 ? responseTimes.Average() : 0;
            result.MaxResponseTime = responseTimes.Count > 0 ? responseTimes.Max() : 0;
            result.MinResponseTime = responseTimes.Count > 0 ? responseTimes.Min() : 0;
            result.ThroughputPerSecond = result.TotalRequests / result.ActualDuration.TotalSeconds;
            
            result.Metrics["SuccessRate"] = (double)successCount / config.ConcurrentUsers * 100;
            result.Metrics["ErrorRate"] = (double)errorCount / config.ConcurrentUsers * 100;
            result.Metrics["P95ResponseTime"] = responseTimes.Count > 0 ? responseTimes.OrderBy(x => x).Skip((int)(responseTimes.Count * 0.95)).First() : 0;
            
            result.Success = errorCount == 0;
            
            await RecordTestExecution("ExecuteStressTest", 
                $"Stress test completed: {successCount}/{config.ConcurrentUsers} successful, avg response: {result.AverageResponseTime:F2}ms", 
                result.Success);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error executing stress test: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.ActualDuration = result.EndTime - result.StartTime;
            
            await RecordTestExecution("ExecuteStressTest", ex.Message, false);
            return result;
        }
    }
    
    public async Task<ValidationResultDto> ValidateSystemBehaviorAsync(List<ValidationRuleDto> validationRules)
    {
        _logger.LogInformation($"Validating system behavior with {validationRules.Count} rules");
        
        var result = new ValidationResultDto
        {
            ValidationStartTime = DateTime.UtcNow,
            RuleResults = new List<RuleValidationResult>()
        };
        
        try
        {
            foreach (var rule in validationRules)
            {
                var ruleResult = await ValidateRule(rule);
                result.RuleResults.Add(ruleResult);
            }
            
            result.ValidationEndTime = DateTime.UtcNow;
            result.ValidationDuration = result.ValidationEndTime - result.ValidationStartTime;
            result.Success = result.RuleResults.All(r => r.IsValid);
            result.PassedRules = result.RuleResults.Count(r => r.IsValid);
            result.FailedRules = result.RuleResults.Count(r => !r.IsValid);
            
            if (!result.Success)
            {
                result.ErrorMessage = string.Join("; ", result.RuleResults.Where(r => !r.IsValid).Select(r => r.ErrorMessage));
            }
            
            await RecordTestExecution("ValidateSystemBehavior", 
                $"Validation completed: {result.PassedRules}/{validationRules.Count} rules passed", 
                result.Success);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error validating system behavior: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ValidationEndTime = DateTime.UtcNow;
            result.ValidationDuration = result.ValidationEndTime - result.ValidationStartTime;
            
            await RecordTestExecution("ValidateSystemBehavior", ex.Message, false);
            return result;
        }
    }
    
    #endregion
    
    #region 测试报告
    
    public async Task<TestReportDto> GenerateTestReportAsync(bool includePerformance = true)
    {
        _logger.LogInformation($"Generating test report (includePerformance: {includePerformance})");
        
        try
        {
            var report = new TestReportDto
            {
                ReportId = Guid.NewGuid().ToString(),
                GeneratedAt = DateTime.UtcNow,
                ReportType = "Comprehensive Test Report",
                TestSummary = await GetTestDataSummaryAsync(),
                TestMetrics = new Dictionary<string, object>(_testMetrics)
            };
            
            // Calculate execution statistics
            if (_testExecutionHistory.Count > 0)
            {
                report.TotalExecutions = _testExecutionHistory.Count;
                report.SuccessfulExecutions = _testExecutionHistory.Count(e => e.Success);
                report.FailedExecutions = _testExecutionHistory.Count(e => !e.Success);
                report.SuccessRate = (double)report.SuccessfulExecutions / report.TotalExecutions * 100;
                
                // Group by test type
                report.ExecutionsByType = _testExecutionHistory.GroupBy(e => e.TestType)
                    .ToDictionary(g => g.Key, g => g.Count());
                
                if (includePerformance)
                {
                    var avgDuration = _testExecutionHistory.Where(e => e.Duration.TotalMilliseconds > 0)
                        .Select(e => e.Duration.TotalMilliseconds).DefaultIfEmpty(0).Average();
                    report.TestMetrics["AverageExecutionTime"] = avgDuration;
                    report.TestMetrics["MaxExecutionTime"] = _testExecutionHistory.Select(e => e.Duration.TotalMilliseconds).DefaultIfEmpty(0).Max();
                    report.TestMetrics["MinExecutionTime"] = _testExecutionHistory.Where(e => e.Duration.TotalMilliseconds > 0).Select(e => e.Duration.TotalMilliseconds).DefaultIfEmpty(0).Min();
                }
            }
            
            // Add system state information
            report.TestMetrics["CurrentTestMode"] = _isTestModeActive;
            report.TestMetrics["TestTimeOffset"] = _testTimeOffsetHours;
            report.TestMetrics["TestDataCounts"] = new Dictionary<string, int>
            {
                { "TestTweets", _testTweets.Count },
                { "TestUsers", _testUsers.Count },
                { "ExecutionHistory", _testExecutionHistory.Count }
            };
            
            await RecordTestExecution("GenerateTestReport", "Test report generated successfully", true);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating test report: {ex.Message}");
            await RecordTestExecution("GenerateTestReport", ex.Message, false);
            return new TestReportDto
            {
                ReportId = Guid.NewGuid().ToString(),
                GeneratedAt = DateTime.UtcNow,
                ReportType = "Error Report",
                TestMetrics = new Dictionary<string, object> { { "Error", ex.Message } }
            };
        }
    }
    
    public async Task<List<TestExecutionRecordDto>> GetTestExecutionHistoryAsync(int days = 7)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-days);
            var filteredHistory = _testExecutionHistory
                .Where(e => e.ExecutionTime >= cutoffTime)
                .OrderByDescending(e => e.ExecutionTime)
                .ToList();
            
            _logger.LogInformation($"Retrieved {filteredHistory.Count} test execution records from the last {days} days");
            return await Task.FromResult(filteredHistory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting test execution history: {ex.Message}");
            return new List<TestExecutionRecordDto>();
        }
    }
    
    public async Task<TestDataExportDto> ExportTestDataAsync(string format = "json")
    {
        _logger.LogInformation($"Exporting test data in {format} format");
        
        try
        {
            var export = new TestDataExportDto
            {
                ExportId = Guid.NewGuid().ToString(),
                ExportTime = DateTime.UtcNow,
                Format = format.ToLower(),
                TestDataSummary = await GetTestDataSummaryAsync()
            };
            
            // Prepare export data based on format
            switch (format.ToLower())
            {
                case "json":
                    export.ExportData = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        TestTweets = _testTweets,
                        TestUsers = _testUsers,
                        ExecutionHistory = _testExecutionHistory,
                        TestMetrics = _testMetrics,
                        SystemState = new
                        {
                            IsTestModeActive = _isTestModeActive,
                            TestTimeOffset = _testTimeOffsetHours
                        }
                    }, new JsonSerializerOptions { WriteIndented = true });
                    break;
                    
                case "csv":
                    var csvBuilder = new System.Text.StringBuilder();
                    csvBuilder.AppendLine("TestType,ExecutionTime,Success,Duration,Result");
                    foreach (var record in _testExecutionHistory)
                    {
                        csvBuilder.AppendLine($"{record.TestType},{record.ExecutionTime:yyyy-MM-dd HH:mm:ss},{record.Success},{record.Duration.TotalMilliseconds},{record.Result?.Replace(",", ";") ?? ""}");
                    }
                    export.ExportData = csvBuilder.ToString();
                    break;
                    
                default:
                    export.ExportData = "Unsupported format. Supported formats: json, csv";
                    break;
            }
            
            export.DataSize = System.Text.Encoding.UTF8.GetByteCount(export.ExportData);
            
            await RecordTestExecution("ExportTestData", $"Test data exported in {format} format, size: {export.DataSize} bytes", true);
            return export;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error exporting test data: {ex.Message}");
            await RecordTestExecution("ExportTestData", ex.Message, false);
            return new TestDataExportDto
            {
                ExportId = Guid.NewGuid().ToString(),
                ExportTime = DateTime.UtcNow,
                Format = format,
                ExportData = $"Export failed: {ex.Message}"
            };
        }
    }
    
    #endregion
    
    #region Private Helper Methods
    
    private async Task RecordTestExecution(string testType, string result, bool success)
    {
        var record = new TestExecutionRecordDto
        {
            TestId = Guid.NewGuid().ToString(),
            TestType = testType,
            TestName = testType,
            ExecutionTime = DateTime.UtcNow,
            ExecutionTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
            Success = success,
            Duration = TimeSpan.Zero, // Instant operations
            Result = result,
            ErrorMessage = success ? string.Empty : result,
            Metrics = new Dictionary<string, object>
            {
                { "TestTimeOffset", _testTimeOffsetHours },
                { "TestTweetsCount", _testTweets.Count },
                { "TestUsersCount", _testUsers.Count },
                { "IsTestModeActive", _isTestModeActive }
            }
        };
        
        _testExecutionHistory.Add(record);
        
        // Keep only the last 1000 records to prevent memory issues
        if (_testExecutionHistory.Count > 1000)
        {
            _testExecutionHistory.RemoveAt(0);
        }
        
        await Task.CompletedTask;
    }
    
    private async Task<TestStepResultDto> ExecuteTestStep(TestStepDto step)
    {
        var stepStart = DateTime.UtcNow;
        var result = new TestStepResultDto
        {
            StepName = step.StepName,
            StartTime = stepStart
        };
        
        try
        {
            // Execute step based on action type
            switch (step.Action.ToLower())
            {
                case "injecttweetdata":
                    var tweets = await GenerateMockTweetDataAsync(
                        step.Parameters.ContainsKey("count") ? (int)step.Parameters["count"] : 10,
                        step.Parameters.ContainsKey("timeRange") ? (TimeRangeDto)step.Parameters["timeRange"] : new TimeRangeDto
                        {
                            StartTimeUtc = ((DateTimeOffset)DateTime.UtcNow.AddHours(-1)).ToUnixTimeSeconds(),
                            EndTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds()
                        }
                    );
                    result.Success = await InjectTestTweetDataAsync(tweets);
                    result.Output = $"Injected {tweets.Count} test tweets";
                    break;
                    
                case "triggerpull":
                    var pullResult = await TriggerPullTaskAsync(true);
                    result.Success = pullResult.Success;
                    result.Output = $"Pull completed: {pullResult.NewTweets} new tweets";
                    break;
                    
                case "triggerreward":
                    var rewardResult = await TriggerRewardTaskAsync(true);
                    result.Success = rewardResult.IsSuccess;
                    result.Output = $"Reward calculated: {rewardResult.TotalCreditsAwarded} credits awarded";
                    break;
                    
                case "settimeoffset":
                    var offset = step.Parameters.ContainsKey("hours") ? (int)step.Parameters["hours"] : 0;
                    result.Success = await SetTestTimeOffsetAsync(offset);
                    result.Output = $"Time offset set to {offset} hours";
                    break;
                    
                default:
                    result.Success = false;
                    result.Output = $"Unknown action: {step.Action}";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Output = $"Step failed: {ex.Message}";
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
        }
        
        return result;
    }
    
    private async Task SimulateUserBehavior(StressTestConfigDto config)
    {
        // Simulate typical user behavior during stress test
        await Task.Delay(100); // Simulate network latency
        
        // Simulate some work based on test type
        if (config.TestOperations.Contains("pull"))
        {
            await TriggerPullTaskAsync(true);
        }
        
        if (config.TestOperations.Contains("reward"))
        {
            await TriggerRewardTaskAsync(true);
        }
        
        await Task.Delay(50); // Simulate processing time
    }
    
    private async Task<RuleValidationResult> ValidateRule(ValidationRuleDto rule)
    {
        var result = new RuleValidationResult
        {
            RuleName = rule.RuleName,
            ValidationTime = DateTime.UtcNow
        };
        
        try
        {
            // Execute validation based on rule type
            switch (rule.RuleType.ToLower())
            {
                case "dataintegrity":
                    result.IsValid = _testTweets.Count > 0 && _testTweets.All(t => !string.IsNullOrEmpty(t.TweetId));
                    result.ValidationMessage = result.IsValid ? "Data integrity validated" : "Data integrity check failed";
                    break;
                    
                case "timeoffset":
                    result.IsValid = _testTimeOffsetHours >= -168 && _testTimeOffsetHours <= 168; // Within a week
                    result.ValidationMessage = result.IsValid ? "Time offset within valid range" : "Time offset out of range";
                    break;
                    
                case "testmode":
                    result.IsValid = _isTestModeActive;
                    result.ValidationMessage = result.IsValid ? "Test mode is active" : "Test mode is not active";
                    break;
                    
                default:
                    result.IsValid = false;
                    result.ErrorMessage = $"Unknown rule type: {rule.RuleType}";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
        }
        
        return await Task.FromResult(result);
    }
    
    #endregion
} 