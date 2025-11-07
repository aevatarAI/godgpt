using System;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Tasks query parameters
/// </summary>
[GenerateSerializer]
public class GoogleTasksQueryDto
{
    /// <summary>
    /// Start time for tasks query (optional, filters by due date)
    /// </summary>
    [Id(0)] public DateTime? StartTime { get; set; }

    /// <summary>
    /// End time for tasks query (optional, filters by due date)
    /// </summary>
    [Id(1)] public DateTime? EndTime { get; set; }

    /// <summary>
    /// Maximum number of tasks to return (optional, uses configured default)
    /// </summary>
    [Id(2)]
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Task list ID to query (optional, defaults to '@default' for default list)
    /// If null, queries all task lists
    /// </summary>
    [Id(3)] public string? TaskListId { get; set; }

    /// <summary>
    /// Whether to show completed tasks
    /// </summary>
    [Id(4)] public bool ShowCompleted { get; set; } = true;

    /// <summary>
    /// Whether to show deleted tasks
    /// </summary>
    [Id(5)] public bool ShowDeleted { get; set; } = false;

    /// <summary>
    /// Whether to show hidden tasks
    /// </summary>
    [Id(6)] public bool ShowHidden { get; set; } = false;

    /// <summary>
    /// Page token for pagination (optional)
    /// </summary>
    [Id(7)] public string PageToken { get; set; } = string.Empty;

    /// <summary>
    /// Updated minimum time (RFC 3339 timestamp)
    /// Only tasks modified after this time will be returned
    /// </summary>
    [Id(8)] public DateTime? UpdatedMin { get; set; }
}
