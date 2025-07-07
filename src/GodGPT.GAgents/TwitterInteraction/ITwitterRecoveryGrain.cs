using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter数据恢复Grain接口
/// 负责系统故障检测、数据恢复和完整性验证
/// </summary>
public interface ITwitterRecoveryGrain : IGrainWithStringKey
{
    #region 故障检测
    
    /// <summary>
    /// 检测指定时间范围内的缺失数据周期
    /// </summary>
    /// <param name="startTimestamp">开始时间戳</param>
    /// <param name="endTimestamp">结束时间戳</param>
    /// <returns>缺失数据周期列表</returns>
    Task<List<MissingPeriodDto>> DetectMissingPeriodsAsync(long startTimestamp, long endTimestamp);
    
    /// <summary>
    /// 检测系统故障
    /// 分析最近的执行历史，识别可能的系统停机时间
    /// </summary>
    /// <returns>系统故障检测结果</returns>
    Task<SystemOutageDto> DetectSystemOutageAsync();
    
    /// <summary>
    /// 检测系统故障 - 指定检查天数
    /// </summary>
    /// <param name="checkDays">检查天数</param>
    /// <returns>系统故障检测结果</returns>
    Task<SystemOutageDto> DetectSystemOutageAsync(int checkDays);
    
    #endregion
    
    #region 数据恢复
    
    /// <summary>
    /// 恢复指定时间周期的数据
    /// </summary>
    /// <param name="startTimestamp">开始时间戳</param>
    /// <param name="endTimestamp">结束时间戳</param>
    /// <param name="forceReprocess">是否强制重新处理已存在的数据</param>
    /// <returns>恢复结果</returns>
    Task<RecoveryResultDto> RecoverPeriodAsync(long startTimestamp, long endTimestamp, bool forceReprocess = false);
    
    /// <summary>
    /// 恢复多个时间周期的数据
    /// </summary>
    /// <param name="periods">时间周期列表</param>
    /// <param name="forceReprocess">是否强制重新处理</param>
    /// <returns>恢复结果</returns>
    Task<RecoveryResultDto> RecoverMultiplePeriodsAsync(List<TimeRangeDto> periods, bool forceReprocess = false);
    
    /// <summary>
    /// 根据恢复请求执行数据恢复
    /// </summary>
    /// <param name="request">恢复请求</param>
    /// <returns>恢复结果</returns>
    Task<RecoveryResultDto> ExecuteRecoveryAsync(RecoveryRequestDto request);
    
    /// <summary>
    /// 自动恢复系统检测到的所有缺失数据
    /// </summary>
    /// <returns>恢复结果</returns>
    Task<RecoveryResultDto> AutoRecoverAllMissingDataAsync();
    
    #endregion
    
    #region 状态验证
    
    /// <summary>
    /// 验证指定时间范围内的数据完整性
    /// </summary>
    /// <param name="startTimestamp">开始时间戳</param>
    /// <param name="endTimestamp">结束时间戳</param>
    /// <returns>数据是否完整</returns>
    Task<bool> ValidateDataIntegrityAsync(long startTimestamp, long endTimestamp);
    
    /// <summary>
    /// 生成数据完整性报告
    /// </summary>
    /// <param name="checkDays">检查天数</param>
    /// <returns>完整性报告</returns>
    Task<DataIntegrityReportDto> GenerateIntegrityReportAsync(int checkDays = 7);
    
    /// <summary>
    /// 验证推文数据和奖励记录的一致性
    /// </summary>
    /// <param name="periodId">周期标识</param>
    /// <returns>一致性检查结果</returns>
    Task<List<DataInconsistencyDto>> ValidateDataConsistencyAsync(string periodId);
    
    #endregion
    
    #region 系统管理
    
    /// <summary>
    /// 获取恢复系统状态
    /// </summary>
    /// <returns>系统状态</returns>
    Task<SystemHealthDto> GetRecoverySystemStatusAsync();
    
    /// <summary>
    /// 获取最近的恢复操作历史
    /// </summary>
    /// <param name="days">查询天数</param>
    /// <returns>恢复历史列表</returns>
    Task<List<RecoveryResultDto>> GetRecoveryHistoryAsync(int days = 30);
    
    /// <summary>
    /// 清理过期的恢复记录
    /// </summary>
    /// <param name="retentionDays">保留天数</param>
    /// <returns>清理的记录数</returns>
    Task<int> CleanupExpiredRecoveryRecordsAsync(int retentionDays = 30);
    
    #endregion
} 