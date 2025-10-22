using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Tasks API response for task lists
/// </summary>
[GenerateSerializer]
public class GoogleTaskListsResponseDto
{
    [Id(0)]
    [JsonProperty("items")]
    public List<GoogleTaskListItemDto> Items { get; set; } = new();

    [Id(1)]
    [JsonProperty("nextPageToken")]
    public string? NextPageToken { get; set; }
}

/// <summary>
/// Google Tasks API response for tasks in a list
/// </summary>
[GenerateSerializer]
public class GoogleTasksResponseDto
{
    [Id(0)]
    [JsonProperty("items")]
    public List<GoogleTaskItemDto> Items { get; set; } = new();

    [Id(1)]
    [JsonProperty("nextPageToken")]
    public string? NextPageToken { get; set; }
}

/// <summary>
/// Google Task List item from API
/// </summary>
[GenerateSerializer]
public class GoogleTaskListItemDto
{
    [Id(0)]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [Id(1)]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [Id(2)]
    [JsonProperty("updated")]
    public string Updated { get; set; } = string.Empty;

    [Id(3)]
    [JsonProperty("selfLink")]
    public string SelfLink { get; set; } = string.Empty;
}

/// <summary>
/// Google Task item from API
/// </summary>
[GenerateSerializer]
public class GoogleTaskItemDto
{
    [Id(0)]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [Id(1)]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [Id(2)]
    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [Id(3)]
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [Id(4)]
    [JsonProperty("due")]
    public string? Due { get; set; }

    [Id(5)]
    [JsonProperty("completed")]
    public string? Completed { get; set; }

    [Id(6)]
    [JsonProperty("updated")]
    public string Updated { get; set; } = string.Empty;

    [Id(7)]
    [JsonProperty("parent")]
    public string? Parent { get; set; }

    [Id(8)]
    [JsonProperty("position")]
    public string? Position { get; set; }

    [Id(9)]
    [JsonProperty("selfLink")]
    public string SelfLink { get; set; } = string.Empty;

    [Id(10)]
    [JsonProperty("deleted")]
    public bool? Deleted { get; set; }

    [Id(11)]
    [JsonProperty("hidden")]
    public bool? Hidden { get; set; }
}

/// <summary>
/// Processed Google Task DTO for client consumption
/// </summary>
[GenerateSerializer]
public class GoogleTaskDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public string Notes { get; set; } = string.Empty;
    [Id(3)] public string Status { get; set; } = string.Empty;
    [Id(4)] public DateTime? Due { get; set; }
    [Id(5)] public DateTime? Completed { get; set; }
    [Id(6)] public DateTime? Created { get; set; }
    [Id(7)] public DateTime? Updated { get; set; }
    [Id(8)] public string Parent { get; set; } = string.Empty;
    [Id(9)] public string Position { get; set; } = string.Empty;
    [Id(10)] public string TaskListId { get; set; } = string.Empty;
    [Id(11)] public string TaskListTitle { get; set; } = string.Empty;
    [Id(12)] public bool IsDeleted { get; set; }
    [Id(13)] public bool IsHidden { get; set; }
}

/// <summary>
/// Google Tasks list result DTO
/// </summary>
[GenerateSerializer]
public class GoogleTasksListDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; } = string.Empty;
    [Id(2)] public List<GoogleTaskDto> Tasks { get; set; } = new();
    [Id(3)] public string? NextPageToken { get; set; }
    [Id(4)] public int TotalTaskLists { get; set; }
}
