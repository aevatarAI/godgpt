using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter测试Grain接口
/// 提供测试环境控制功能，包括时间模拟、数据注入、任务触发等
/// </summary>
public interface ITwitterTestingGrain : IGrainWithStringKey
{
    #region 时间控制测试
    
    /// <summary>
    /// 设置测试时间偏移
    /// </summary>
    /// <param name="offsetHours">时间偏移小时数（可为负数）</param>
    /// <returns>设置成功与否</returns>
    Task<bool> SetTestTimeOffsetAsync(int offsetHours);
    
    /// <summary>
    /// 获取当前测试时间戳
    /// </summary>
    /// <returns>当前测试时间戳</returns>
    Task<long> GetCurrentTestTimestampAsync();
    
    /// <summary>
    /// 模拟时间流逝
    /// </summary>
    /// <param name="minutes">前进的分钟数</param>
    /// <returns>操作成功与否</returns>
    Task<bool> SimulateTimePassageAsync(int minutes);
    
    /// <summary>
    /// 重置时间偏移为零
    /// </summary>
    /// <returns>重置成功与否</returns>
    Task<bool> ResetTimeOffsetAsync();
    
    #endregion
    
    #region 数据模拟
    
    /// <summary>
    /// 注入测试推文数据
    /// </summary>
    /// <param name="testTweets">测试推文列表</param>
    /// <returns>注入成功与否</returns>
    Task<bool> InjectTestTweetDataAsync(List<TweetRecord> testTweets);
    
    /// <summary>
    /// 生成模拟推文数据
    /// </summary>
    /// <param name="count">生成数量</param>
    /// <param name="timeRange">时间范围</param>
    /// <param name="tweetType">推文类型</param>
    /// <returns>生成的测试推文</returns>
    Task<List<TweetRecord>> GenerateMockTweetDataAsync(int count, TimeRangeDto timeRange, TweetType tweetType = TweetType.Original);
    
    /// <summary>
    /// 注入测试用户数据
    /// </summary>
    /// <param name="testUsers">测试用户列表</param>
    /// <returns>注入成功与否</returns>
    Task<bool> InjectTestUserDataAsync(List<UserInfoDto> testUsers);
    
    /// <summary>
    /// 清理所有测试数据
    /// </summary>
    /// <returns>清理成功与否</returns>
    Task<bool> ClearAllTestDataAsync();
    
    /// <summary>
    /// 获取测试数据摘要
    /// </summary>
    /// <returns>测试数据摘要</returns>
    Task<TestDataSummaryDto> GetTestDataSummaryAsync();
    
    #endregion
    
    #region 任务触发测试
    
    /// <summary>
    /// 手动触发推文拉取任务
    /// </summary>
    /// <param name="useTestTime">是否使用测试时间</param>
    /// <returns>拉取结果</returns>
    Task<PullTweetResultDto> TriggerPullTaskAsync(bool useTestTime = true);
    
    /// <summary>
    /// 手动触发奖励计算任务
    /// </summary>
    /// <param name="useTestTime">是否使用测试时间</param>
    /// <returns>计算结果</returns>
    Task<RewardCalculationResultDto> TriggerRewardTaskAsync(bool useTestTime = true);
    
    /// <summary>
    /// 触发指定时间范围的数据处理
    /// </summary>
    /// <param name="timeRange">时间范围</param>
    /// <param name="includePull">是否包含推文拉取</param>
    /// <param name="includeReward">是否包含奖励计算</param>
    /// <returns>处理结果</returns>
    Task<TestProcessingResultDto> TriggerRangeProcessingAsync(TimeRangeDto timeRange, bool includePull = true, bool includeReward = true);
    
    #endregion
    
    #region 状态控制
    
    /// <summary>
    /// 重置所有任务状态
    /// </summary>
    /// <returns>重置成功与否</returns>
    Task<bool> ResetAllTaskStatesAsync();
    
    /// <summary>
    /// 重置执行历史记录
    /// </summary>
    /// <returns>重置成功与否</returns>
    Task<bool> ResetExecutionHistoryAsync();
    
    /// <summary>
    /// 启用/禁用测试模式
    /// </summary>
    /// <param name="enabled">是否启用</param>
    /// <returns>设置成功与否</returns>
    Task<bool> SetTestModeAsync(bool enabled);
    
    /// <summary>
    /// 检查是否处于测试模式
    /// </summary>
    /// <returns>是否测试模式</returns>
    Task<bool> IsTestModeActiveAsync();
    
    #endregion
    
    #region 场景测试
    
    /// <summary>
    /// 执行完整的端到端测试场景
    /// </summary>
    /// <param name="scenario">测试场景配置</param>
    /// <returns>测试结果</returns>
    Task<TestScenarioResultDto> ExecuteTestScenarioAsync(TestScenarioDto scenario);
    
    /// <summary>
    /// 执行压力测试
    /// </summary>
    /// <param name="config">压力测试配置</param>
    /// <returns>压力测试结果</returns>
    Task<StressTestResultDto> ExecuteStressTestAsync(StressTestConfigDto config);
    
    /// <summary>
    /// 验证系统行为
    /// </summary>
    /// <param name="validationRules">验证规则</param>
    /// <returns>验证结果</returns>
    Task<ValidationResultDto> ValidateSystemBehaviorAsync(List<ValidationRuleDto> validationRules);
    
    #endregion
    
    #region 测试报告
    
    /// <summary>
    /// 生成测试报告
    /// </summary>
    /// <param name="includePerformance">是否包含性能指标</param>
    /// <returns>测试报告</returns>
    Task<TestReportDto> GenerateTestReportAsync(bool includePerformance = true);
    
    /// <summary>
    /// 获取测试执行历史
    /// </summary>
    /// <param name="days">查询天数</param>
    /// <returns>测试历史</returns>
    Task<List<TestExecutionRecordDto>> GetTestExecutionHistoryAsync(int days = 7);
    
    /// <summary>
    /// 导出测试数据
    /// </summary>
    /// <param name="format">导出格式</param>
    /// <returns>导出的数据</returns>
    Task<TestDataExportDto> ExportTestDataAsync(string format = "json");
    
    #endregion
} 