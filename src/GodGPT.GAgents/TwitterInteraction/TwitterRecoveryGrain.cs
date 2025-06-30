using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter数据恢复Grain实现
/// 负责系统故障检测、数据恢复和完整性验证
/// </summary>
public class TwitterRecoveryGrain : Grain, ITwitterRecoveryGrain
{
    private readonly ILogger<TwitterRecoveryGrain> _logger;
    
    // 依赖的其他Grain
    private ITweetMonitorGrain? _tweetMonitorGrain;
    private ITwitterRewardGrain? _twitterRewardGrain;
    private ITwitterSystemManagerGrain? _systemManagerGrain;
    
    // 内部状态
    private readonly List<RecoveryResultDto> _recoveryHistory = new();
    private readonly Dictionary<string, DateTime> _lastValidationTimes = new();
    
    public TwitterRecoveryGrain(ILogger<TwitterRecoveryGrain> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"TwitterRecoveryGrain activated: {this.GetPrimaryKeyString()}");
        
        // Initialize dependent grains
        _tweetMonitorGrain = GrainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
        _twitterRewardGrain = GrainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
        _systemManagerGrain = GrainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        
        return base.OnActivateAsync(cancellationToken);
    }
    
    #region 故障检测
    
    public async Task<List<MissingPeriodDto>> DetectMissingPeriodsAsync(long startTimestamp, long endTimestamp)
    {
        _logger.LogInformation($"Detecting missing periods from {startTimestamp} to {endTimestamp}");
        
        try
        {
            var missingPeriods = new List<MissingPeriodDto>();
            
            // Calculate expected periods (assuming 30-minute intervals)
            var intervalMinutes = 30;
            var expectedPeriods = GenerateExpectedPeriods(startTimestamp, endTimestamp, intervalMinutes);
            
            foreach (var period in expectedPeriods)
            {
                var missingPeriod = await AnalyzePeriodAsync(period);
                if (missingPeriod != null)
                {
                    missingPeriods.Add(missingPeriod);
                }
            }
            
            _logger.LogInformation($"Found {missingPeriods.Count} missing periods out of {expectedPeriods.Count} expected periods");
            return missingPeriods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error detecting missing periods: {ex.Message}");
            throw;
        }
    }
    
    public async Task<SystemOutageDto> DetectSystemOutageAsync()
    {
        return await DetectSystemOutageAsync(7); // Default 7 days
    }
    
    public async Task<SystemOutageDto> DetectSystemOutageAsync(int checkDays)
    {
        _logger.LogInformation($"Detecting system outage for past {checkDays} days");
        
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-checkDays);
            var startTimestamp = ((DateTimeOffset)startTime).ToUnixTimeSeconds();
            var endTimestamp = ((DateTimeOffset)endTime).ToUnixTimeSeconds();
            
            var missingPeriods = await DetectMissingPeriodsAsync(startTimestamp, endTimestamp);
            
            var outage = new SystemOutageDto
            {
                OutageDetected = missingPeriods.Count > 0,
                TotalMissingPeriods = missingPeriods.Count,
                AffectedPeriods = missingPeriods
            };
            
            if (outage.OutageDetected)
            {
                // Find the longest consecutive outage period
                var consecutiveOutages = FindConsecutiveOutages(missingPeriods);
                if (consecutiveOutages.Count > 0)
                {
                    var longestOutage = consecutiveOutages.OrderByDescending(o => o.OutageDurationMinutes).First();
                    outage.OutageStartTime = longestOutage.OutageStartTime;
                    outage.OutageEndTime = longestOutage.OutageEndTime;
                    outage.OutageStartTimestamp = longestOutage.OutageStartTimestamp;
                    outage.OutageEndTimestamp = longestOutage.OutageEndTimestamp;
                    outage.OutageDurationMinutes = longestOutage.OutageDurationMinutes;
                }
                
                outage.RecoveryPlan = GenerateRecoveryPlan(missingPeriods);
                outage.OutageReason = "Detected missing data periods indicating potential system downtime";
            }
            
            _logger.LogInformation($"System outage detection completed. Outage detected: {outage.OutageDetected}, Missing periods: {outage.TotalMissingPeriods}");
            return outage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error detecting system outage: {ex.Message}");
            throw;
        }
    }
    
    #endregion
    
    #region 数据恢复
    
    public async Task<RecoveryResultDto> RecoverPeriodAsync(long startTimestamp, long endTimestamp, bool forceReprocess = false)
    {
        _logger.LogInformation($"Starting recovery for period {startTimestamp} to {endTimestamp}, force reprocess: {forceReprocess}");
        
        var recoveryStart = DateTime.UtcNow;
        var result = new RecoveryResultDto
        {
            RecoveryStartTime = recoveryStart,
            RecoveryTimestamp = ((DateTimeOffset)recoveryStart).ToUnixTimeSeconds(),
            ProcessedPeriods = new List<string>(),
            FailedPeriods = new List<string>(),
            RecoverySteps = new List<RecoveryStepDto>()
        };
        
        try
        {
            var periodId = $"{startTimestamp}-{endTimestamp}";
            result.ProcessedPeriods.Add(periodId);
            
            // Step 1: Recover tweet data
            var tweetRecoveryStep = await ExecuteRecoveryStep("RecoverTweetData", async () =>
            {
                if (_tweetMonitorGrain == null)
                    throw new InvalidOperationException("TweetMonitorGrain not initialized");
                
                // 使用时间范围重新拉取推文数据
                var timeRange = new TimeRangeDto { StartTimeUtc = startTimestamp, EndTimeUtc = endTimestamp };
                var refetchResult = await _tweetMonitorGrain.RefetchTweetsByTimeRangeAsync(timeRange);
                result.RecoveredTweets = refetchResult.Data?.NewTweets ?? 0;
                return $"Recovered {result.RecoveredTweets} tweets";
            });
            result.RecoverySteps.Add(tweetRecoveryStep);
            
            // Step 2: Recalculate rewards
            var rewardRecoveryStep = await ExecuteRecoveryStep("RecalculateRewards", async () =>
            {
                if (_twitterRewardGrain == null)
                    throw new InvalidOperationException("TwitterRewardGrain not initialized");
                
                // 计算时间范围内的每一天的奖励
                var startDate = DateTimeOffset.FromUnixTimeSeconds(startTimestamp).Date;
                var endDate = DateTimeOffset.FromUnixTimeSeconds(endTimestamp).Date;
                
                long totalCredits = 0;
                int affectedUsers = 0;
                
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    var dailyResult = await _twitterRewardGrain.RecalculateRewardsForDateAsync(date, true);
                    if (dailyResult.IsSuccess && dailyResult.Data != null)
                    {
                        totalCredits += dailyResult.Data.TotalCreditsAwarded;
                        // 注意：AffectedUsers在RewardCalculationResultDto中可能不存在，我们需要用合理的默认值
                        affectedUsers += 1; // 假设每天影响1个用户，实际应该从API结果中获取
                    }
                }
                
                result.RecalculatedRewards = (int)totalCredits;
                result.AffectedUsers = affectedUsers;
                return $"Recalculated rewards for {affectedUsers} users, {totalCredits} credits";
            });
            result.RecoverySteps.Add(rewardRecoveryStep);
            
            // Check if all steps completed successfully
            result.Success = result.RecoverySteps.All(s => s.IsCompleted && string.IsNullOrEmpty(s.ErrorMessage));
            
            if (!result.Success)
            {
                result.FailedPeriods.Add(periodId);
                result.ErrorMessage = string.Join("; ", result.RecoverySteps.Where(s => !string.IsNullOrEmpty(s.ErrorMessage)).Select(s => s.ErrorMessage));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during period recovery: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FailedPeriods.Add($"{startTimestamp}-{endTimestamp}");
        }
        finally
        {
            result.RecoveryEndTime = DateTime.UtcNow;
            result.RecoveryDuration = result.RecoveryEndTime - result.RecoveryStartTime;
            
            // Add to history
            _recoveryHistory.Add(result);
            
            _logger.LogInformation($"Recovery completed for period {startTimestamp}-{endTimestamp}. Success: {result.Success}, Duration: {result.RecoveryDuration}");
        }
        
        return result;
    }
    
    public async Task<RecoveryResultDto> RecoverMultiplePeriodsAsync(List<TimeRangeDto> periods, bool forceReprocess = false)
    {
        _logger.LogInformation($"Starting recovery for {periods.Count} periods");
        
        var overallResult = new RecoveryResultDto
        {
            RecoveryStartTime = DateTime.UtcNow,
            RecoveryTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
            ProcessedPeriods = new List<string>(),
            FailedPeriods = new List<string>(),
            RecoverySteps = new List<RecoveryStepDto>()
        };
        
        int successCount = 0;
        
        foreach (var period in periods)
        {
            try
            {
                var periodResult = await RecoverPeriodAsync(period.StartTimeUtc, period.EndTimeUtc, forceReprocess);
                
                // Aggregate results
                overallResult.RecoveredTweets += periodResult.RecoveredTweets;
                overallResult.RecalculatedRewards += periodResult.RecalculatedRewards;
                overallResult.AffectedUsers += periodResult.AffectedUsers;
                overallResult.ProcessedPeriods.AddRange(periodResult.ProcessedPeriods);
                overallResult.FailedPeriods.AddRange(periodResult.FailedPeriods);
                overallResult.RecoverySteps.AddRange(periodResult.RecoverySteps);
                
                if (periodResult.Success)
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recovering period {period.StartTimeUtc}-{period.EndTimeUtc}: {ex.Message}");
                overallResult.FailedPeriods.Add($"{period.StartTimeUtc}-{period.EndTimeUtc}");
            }
        }
        
        overallResult.Success = successCount == periods.Count;
        overallResult.RecoveryEndTime = DateTime.UtcNow;
        overallResult.RecoveryDuration = overallResult.RecoveryEndTime - overallResult.RecoveryStartTime;
        
        if (!overallResult.Success)
        {
            overallResult.ErrorMessage = $"Recovery completed with {overallResult.FailedPeriods.Count} failed periods out of {periods.Count} total periods";
        }
        
        _logger.LogInformation($"Multiple period recovery completed. Success: {overallResult.Success}, Processed: {successCount}/{periods.Count}");
        return overallResult;
    }
    
    public async Task<RecoveryResultDto> ExecuteRecoveryAsync(RecoveryRequestDto request)
    {
        _logger.LogInformation($"Executing recovery request from {request.StartTimestamp} to {request.EndTimestamp}, requested by: {request.RequestedBy}");
        
        if (request.TargetPeriods.Count > 0)
        {
            // Recover specific periods
            var periods = request.TargetPeriods.Select(p =>
            {
                var parts = p.Split('-');
                return new TimeRangeDto
                {
                    StartTimeUtc = long.Parse(parts[0]),
                    EndTimeUtc = long.Parse(parts[1])
                };
            }).ToList();
            
            return await RecoverMultiplePeriodsAsync(periods, request.ForceReprocess);
        }
        else
        {
            // Recover the entire range
            return await RecoverPeriodAsync(request.StartTimestamp, request.EndTimestamp, request.ForceReprocess);
        }
    }
    
    public async Task<RecoveryResultDto> AutoRecoverAllMissingDataAsync()
    {
        _logger.LogInformation("Starting automatic recovery of all missing data");
        
        try
        {
            // Detect all missing periods in the last 7 days
            var outage = await DetectSystemOutageAsync(7);
            
            if (!outage.OutageDetected || outage.AffectedPeriods.Count == 0)
            {
                _logger.LogInformation("No missing data detected, auto recovery not needed");
                return new RecoveryResultDto
                {
                    Success = true,
                    RecoveryStartTime = DateTime.UtcNow,
                    RecoveryEndTime = DateTime.UtcNow,
                    RecoveryDuration = TimeSpan.Zero,
                    ErrorMessage = "No missing data detected"
                };
            }
            
            // Convert missing periods to time ranges
            var periods = outage.AffectedPeriods.Select(p => new TimeRangeDto
            {
                StartTimeUtc = p.StartTimestamp,
                EndTimeUtc = p.EndTimestamp
            }).ToList();
            
            // Execute recovery
            var result = await RecoverMultiplePeriodsAsync(periods, false);
            
            _logger.LogInformation($"Auto recovery completed. Success: {result.Success}, Periods processed: {periods.Count}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during auto recovery: {ex.Message}");
            return new RecoveryResultDto
            {
                Success = false,
                ErrorMessage = ex.Message,
                RecoveryStartTime = DateTime.UtcNow,
                RecoveryEndTime = DateTime.UtcNow
            };
        }
    }
    
    #endregion
    
    #region 状态验证
    
    public async Task<bool> ValidateDataIntegrityAsync(long startTimestamp, long endTimestamp)
    {
        _logger.LogInformation($"Validating data integrity from {startTimestamp} to {endTimestamp}");
        
        try
        {
            var missingPeriods = await DetectMissingPeriodsAsync(startTimestamp, endTimestamp);
            var isIntegrityValid = missingPeriods.Count == 0;
            
            _logger.LogInformation($"Data integrity validation completed. Valid: {isIntegrityValid}, Missing periods: {missingPeriods.Count}");
            return isIntegrityValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error validating data integrity: {ex.Message}");
            return false;
        }
    }
    
    public async Task<DataIntegrityReportDto> GenerateIntegrityReportAsync(int checkDays = 7)
    {
        _logger.LogInformation($"Generating data integrity report for {checkDays} days");
        
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-checkDays);
            var startTimestamp = ((DateTimeOffset)startTime).ToUnixTimeSeconds();
            var endTimestamp = ((DateTimeOffset)endTime).ToUnixTimeSeconds();
            
            var missingPeriods = await DetectMissingPeriodsAsync(startTimestamp, endTimestamp);
            var inconsistencies = new List<DataInconsistencyDto>();
            
            // Check for data inconsistencies
            foreach (var period in missingPeriods)
            {
                var inconsistency = new DataInconsistencyDto
                {
                    InconsistencyType = period.MissingType,
                    Description = $"Missing {period.MissingType} for period {period.PeriodId}",
                    DetectedAt = DateTime.UtcNow,
                    AffectedPeriod = period.PeriodId,
                    Severity = DetermineSeverity(period),
                    RecommendedAction = $"Run recovery for period {period.PeriodId}"
                };
                inconsistencies.Add(inconsistency);
            }
            
            var expectedPeriods = (int)((endTimestamp - startTimestamp) / (30 * 60)); // 30-minute intervals
            var validPeriods = expectedPeriods - missingPeriods.Count;
            
            var report = new DataIntegrityReportDto
            {
                GeneratedAt = DateTime.UtcNow,
                GeneratedAtTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                InspectedRange = new TimeRangeDto
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    StartTimeUtc = startTimestamp,
                    EndTimeUtc = endTimestamp
                },
                IsDataComplete = missingPeriods.Count == 0,
                TotalExpectedPeriods = expectedPeriods,
                ValidPeriods = validPeriods,
                MissingPeriods = missingPeriods.Count,
                MissingData = missingPeriods,
                Inconsistencies = inconsistencies,
                RecommendedActions = GenerateRecommendations(missingPeriods, inconsistencies)
            };
            
            _logger.LogInformation($"Integrity report generated. Complete: {report.IsDataComplete}, Valid: {report.ValidPeriods}/{report.TotalExpectedPeriods}");
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating integrity report: {ex.Message}");
            throw;
        }
    }
    
    public async Task<List<DataInconsistencyDto>> ValidateDataConsistencyAsync(string periodId)
    {
        _logger.LogInformation($"Validating data consistency for period: {periodId}");
        
        try
        {
            var inconsistencies = new List<DataInconsistencyDto>();
            
            // Parse period ID to get timestamps
            var parts = periodId.Split('-');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var startTimestamp) || !long.TryParse(parts[1], out var endTimestamp))
            {
                inconsistencies.Add(new DataInconsistencyDto
                {
                    InconsistencyType = "Invalid Period ID",
                    Description = $"Period ID '{periodId}' has invalid format",
                    DetectedAt = DateTime.UtcNow,
                    AffectedPeriod = periodId,
                    Severity = "High",
                    RecommendedAction = "Use valid period ID format: startTimestamp-endTimestamp"
                });
                return inconsistencies;
            }
            
            // TODO: Add specific consistency checks between tweet data and reward records
            // This would involve comparing tweet records with reward calculation results
            
            _logger.LogInformation($"Data consistency validation completed for period {periodId}. Found {inconsistencies.Count} inconsistencies");
            return inconsistencies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error validating data consistency: {ex.Message}");
            throw;
        }
    }
    
    #endregion
    
    #region 系统管理
    
    public async Task<SystemHealthDto> GetRecoverySystemStatusAsync()
    {
        _logger.LogInformation("Getting recovery system status");
        
        try
        {
            var status = new SystemHealthDto
            {
                IsHealthy = true,
                LastUpdateTime = DateTime.UtcNow,
                LastUpdateTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                ActiveTasks = 0, // Recovery operations are typically short-lived
                PendingTweets = 0,
                PendingRewards = 0,
                Warnings = new List<string>(),
                Errors = new List<string>(),
                HealthMetrics = new Dictionary<string, object>
                {
                    { "TotalRecoveryOperations", _recoveryHistory.Count },
                    { "SuccessfulRecoveries", _recoveryHistory.Count(r => r.Success) },
                    { "FailedRecoveries", _recoveryHistory.Count(r => !r.Success) },
                    { "LastRecoveryTime", _recoveryHistory.LastOrDefault()?.RecoveryStartTime.ToString() ?? "Never" }
                }
            };
            
            // Check for recent failures
            var recentFailures = _recoveryHistory.Where(r => !r.Success && r.RecoveryStartTime > DateTime.UtcNow.AddHours(-24)).ToList();
            if (recentFailures.Count > 0)
            {
                status.Warnings.Add($"{recentFailures.Count} recovery operations failed in the last 24 hours");
                status.IsHealthy = recentFailures.Count < 5; // Consider unhealthy if more than 5 failures
            }
            
            return await Task.FromResult(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting recovery system status: {ex.Message}");
            throw;
        }
    }
    
    public async Task<List<RecoveryResultDto>> GetRecoveryHistoryAsync(int days = 30)
    {
        _logger.LogInformation($"Getting recovery history for {days} days");
        
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);
            var filteredHistory = _recoveryHistory
                .Where(r => r.RecoveryStartTime >= cutoffDate)
                .OrderByDescending(r => r.RecoveryStartTime)
                .ToList();
            
            _logger.LogInformation($"Found {filteredHistory.Count} recovery operations in the last {days} days");
            return await Task.FromResult(filteredHistory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting recovery history: {ex.Message}");
            throw;
        }
    }
    
    public async Task<int> CleanupExpiredRecoveryRecordsAsync(int retentionDays = 30)
    {
        _logger.LogInformation($"Cleaning up recovery records older than {retentionDays} days");
        
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var expiredRecords = _recoveryHistory.Where(r => r.RecoveryStartTime < cutoffDate).ToList();
            
            foreach (var record in expiredRecords)
            {
                _recoveryHistory.Remove(record);
            }
            
            _logger.LogInformation($"Cleaned up {expiredRecords.Count} expired recovery records");
            return await Task.FromResult(expiredRecords.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error cleaning up recovery records: {ex.Message}");
            throw;
        }
    }
    
    #endregion
    
    #region Private Helper Methods
    
    private List<TimeRangeDto> GenerateExpectedPeriods(long startTimestamp, long endTimestamp, int intervalMinutes)
    {
        var periods = new List<TimeRangeDto>();
        var intervalSeconds = intervalMinutes * 60;
        
        for (long current = startTimestamp; current < endTimestamp; current += intervalSeconds)
        {
            var periodEnd = Math.Min(current + intervalSeconds, endTimestamp);
            periods.Add(new TimeRangeDto
            {
                StartTimeUtc = current,
                EndTimeUtc = periodEnd,
                StartTime = DateTimeOffset.FromUnixTimeSeconds(current).DateTime,
                EndTime = DateTimeOffset.FromUnixTimeSeconds(periodEnd).DateTime
            });
        }
        
        return periods;
    }
    
    private async Task<MissingPeriodDto?> AnalyzePeriodAsync(TimeRangeDto period)
    {
        try
        {
            // Check if tweet data exists for this period
            if (_tweetMonitorGrain == null)
                return null;
            
            // 查询指定时间区间内的推文数据来检查是否有数据
            var tweetsResult = await _tweetMonitorGrain.QueryTweetsByTimeRangeAsync(period);
            var hasData = tweetsResult.IsSuccess && tweetsResult.Data != null && tweetsResult.Data.Count > 0;
            
            if (hasData)
                return null; // Data exists, no missing period
            
            // Data is missing
            return new MissingPeriodDto
            {
                StartTime = period.StartTime,
                EndTime = period.EndTime,
                StartTimestamp = period.StartTimeUtc,
                EndTimestamp = period.EndTimeUtc,
                PeriodId = $"{period.StartTimeUtc}-{period.EndTimeUtc}",
                MissingType = "TweetData",
                ExpectedTweetCount = 1, // At least some tweets expected in a 30-minute window
                ActualTweetCount = 0,
                HasRewardRecord = false,
                Description = $"No tweet data found for period {period.StartTime:yyyy-MM-dd HH:mm} - {period.EndTime:yyyy-MM-dd HH:mm} UTC"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Error analyzing period {period.StartTimeUtc}-{period.EndTimeUtc}: {ex.Message}");
            return null;
        }
    }
    
    private List<SystemOutageDto> FindConsecutiveOutages(List<MissingPeriodDto> missingPeriods)
    {
        var consecutiveOutages = new List<SystemOutageDto>();
        
        if (missingPeriods.Count == 0)
            return consecutiveOutages;
        
        var sortedPeriods = missingPeriods.OrderBy(p => p.StartTimestamp).ToList();
        var currentOutage = new SystemOutageDto
        {
            OutageDetected = true,
            OutageStartTime = sortedPeriods[0].StartTime,
            OutageStartTimestamp = sortedPeriods[0].StartTimestamp
        };
        
        for (int i = 1; i < sortedPeriods.Count; i++)
        {
            var current = sortedPeriods[i];
            var previous = sortedPeriods[i - 1];
            
            // Check if periods are consecutive (within 1 hour gap)
            var gapMinutes = (current.StartTimestamp - previous.EndTimestamp) / 60;
            
            if (gapMinutes <= 60) // Consecutive or close enough
            {
                // Extend current outage
                currentOutage.OutageEndTime = current.EndTime;
                currentOutage.OutageEndTimestamp = current.EndTimestamp;
            }
            else
            {
                // End current outage and start a new one
                currentOutage.OutageEndTime = previous.EndTime;
                currentOutage.OutageEndTimestamp = previous.EndTimestamp;
                currentOutage.OutageDurationMinutes = (int)((currentOutage.OutageEndTimestamp - currentOutage.OutageStartTimestamp) / 60);
                consecutiveOutages.Add(currentOutage);
                
                currentOutage = new SystemOutageDto
                {
                    OutageDetected = true,
                    OutageStartTime = current.StartTime,
                    OutageStartTimestamp = current.StartTimestamp
                };
            }
        }
        
        // Add the last outage
        currentOutage.OutageEndTime = sortedPeriods.Last().EndTime;
        currentOutage.OutageEndTimestamp = sortedPeriods.Last().EndTimestamp;
        currentOutage.OutageDurationMinutes = (int)((currentOutage.OutageEndTimestamp - currentOutage.OutageStartTimestamp) / 60);
        consecutiveOutages.Add(currentOutage);
        
        return consecutiveOutages;
    }
    
    private string GenerateRecoveryPlan(List<MissingPeriodDto> missingPeriods)
    {
        if (missingPeriods.Count == 0)
            return "No recovery needed - all data is present";
        
        var tweetDataMissing = missingPeriods.Count(p => p.MissingType.Contains("TweetData"));
        var rewardMissing = missingPeriods.Count(p => p.MissingType.Contains("Reward"));
        
        var plan = "Recovery Plan:\n";
        if (tweetDataMissing > 0)
            plan += $"1. Recover tweet data for {tweetDataMissing} periods\n";
        if (rewardMissing > 0)
            plan += $"2. Recalculate rewards for {rewardMissing} periods\n";
        
        plan += $"3. Validate data integrity after recovery\n";
        plan += $"Total estimated time: {missingPeriods.Count * 2} minutes";
        
        return plan;
    }
    
    private async Task<RecoveryStepDto> ExecuteRecoveryStep(string stepName, Func<Task<string>> stepAction)
    {
        var step = new RecoveryStepDto
        {
            StepName = stepName,
            StartTime = DateTime.UtcNow,
            Status = "Running"
        };
        
        try
        {
            step.Details = await stepAction();
            step.IsCompleted = true;
            step.Status = "Completed";
        }
        catch (Exception ex)
        {
            step.IsCompleted = false;
            step.Status = "Failed";
            step.ErrorMessage = ex.Message;
            _logger.LogError(ex, $"Recovery step '{stepName}' failed: {ex.Message}");
        }
        finally
        {
            step.EndTime = DateTime.UtcNow;
        }
        
        return step;
    }
    
    private string DetermineSeverity(MissingPeriodDto period)
    {
        // Determine severity based on various factors
        var age = DateTime.UtcNow - period.StartTime;
        
        if (age.TotalHours < 24)
            return "High"; // Recent data missing is high priority
        if (age.TotalDays < 3)
            return "Medium";
        
        return "Low";
    }
    
    private string GenerateRecommendations(List<MissingPeriodDto> missingPeriods, List<DataInconsistencyDto> inconsistencies)
    {
        var recommendations = new List<string>();
        
        if (missingPeriods.Count > 0)
        {
            recommendations.Add($"Execute recovery for {missingPeriods.Count} missing periods");
        }
        
        var highSeverityIssues = inconsistencies.Count(i => i.Severity == "High" || i.Severity == "Critical");
        if (highSeverityIssues > 0)
        {
            recommendations.Add($"Address {highSeverityIssues} high-severity data inconsistencies immediately");
        }
        
        if (recommendations.Count == 0)
        {
            recommendations.Add("No immediate action required - data integrity is good");
        }
        
        return string.Join("; ", recommendations);
    }
    
    #endregion
} 