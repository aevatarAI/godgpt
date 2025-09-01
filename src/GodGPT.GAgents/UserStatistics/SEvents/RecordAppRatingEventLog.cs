namespace Aevatar.Application.Grains.UserStatistics.SEvents;


[GenerateSerializer]
public class RecordAppRatingEventLog : UserStatisticsEventLog
{
    [Id(0)] public string Platform { get; set; } = string.Empty;
    [Id(1)] public string DeviceId { get; set; }
    [Id(2)] public DateTime RatingTime { get; set; }
    [Id(3)] public int RatingCount { get; set; }
    [Id(4)] public bool? IsRealUser { get; set; }
    [Id(5)] public Guid? RealUserId { get; set; }
}