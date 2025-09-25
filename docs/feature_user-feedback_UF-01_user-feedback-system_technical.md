# User Feedback System Technical Design Document

## Document Information

| Item | Content |
|----|---|
| Technical Document ID | TECH-UF-UF-01 |
| Related Requirement ID | REQ-UF-UF-01 |
| Related Task ID | UF-01 |
| Related Branch | feature/user-feedback |
| Priority | P1 |
| Status | In Design |
| Created Date | 2025-09-24 |
| Last Updated | 2025-09-24 |
| Author | HyperEcho |

## 1. System Architecture

### 1.1 Overall Architecture

The user feedback system adopts the GAgent architecture pattern, creating independent UserFeedbackGAgent instances for each user to manage user feedback data and behaviors. The system mainly includes the following components:

- **UserFeedbackGAgent**: Core business logic processor, managing user feedback data
- **UserFeedbackState**: User feedback state data storage
- **UserFeedbackEventLog**: Feedback operation event log
- **Feedback Frequency Control**: Mechanism to limit triggering to at most once within 14 days
- **Data Archiving**: Historical feedback data management

### 1.2 Module Division

```
UserFeedbackGAgent
├── Feedback Data Management
│   ├── Submit Feedback
│   ├── Query Feedback History
│   └── Feedback Data Archiving
├── Frequency Control
│   ├── Check Trigger Frequency
│   ├── Update Last Trigger Time
│   └── Frequency Limit Validation
└── Data Query
    ├── Get Current Feedback Status
    ├── Query Historical Feedback
    └── Check Feedback Permissions
```

### 1.3 Compatibility with Existing Systems

- Follows the project's existing GAgent architecture pattern, inheriting from `GAgentBase<TState, TEventLog>`
- Uses the same storage provider configuration: `PubSubStore` and `LogStorage`
- Adopts Orleans Grain pattern, supporting distributed deployment
- Integrates with existing user systems, associating feedback data through user ID
- Reuses existing logging and error handling mechanisms

## 2. Interface Specifications

### 2.1 API Interfaces

> **Important Note**: The following HTTP interface definitions are for reference specification only and **are NOT implemented in this project**. HTTP interfaces are provided by the external Aevatar-Station platform, this project only implements GAgent business logic.

#### Interface List

| Interface Name | Request Method | Interface Path | Description |
|---|---|---|---|
| Submit User Feedback | POST | /api/user-feedback/submit | Submit user feedback data |
| Check Feedback Eligibility | GET | /api/user-feedback/check-eligibility | Check if user can submit feedback |
| Get Feedback History | GET | /api/user-feedback/history | Get user feedback history records |

#### Interface Details

##### Submit User Feedback

**Request Parameters:**

```json
{
  "userId": "string - User ID",
  "feedbackType": "string - Feedback type (Cancel/Change)",
  "reason": "string - Feedback reason",
  "response": "string - User detailed feedback content (optional, max 512 characters)",
  "contactRequested": "boolean - Whether contact is requested",
  "email": "string - Contact email (required when contactRequested is true)"
}
```

**Response Result:**

```json
{
  "success": true,
  "message": "Feedback submitted successfully",
  "data": {
    "feedbackId": "string - Feedback ID",
    "submittedAt": "datetime - Submission time"
  }
}
```

**Error Codes:**

| Error Code | Description | Solution |
|----|---|---|
| 400 | Invalid request parameters | Check request parameter format |
| 429 | Submission frequency exceeded | Only one feedback allowed within 14 days |
| 500 | Internal server error | Contact technical support |

##### Check Feedback Eligibility

**Request Parameters:**

```json
{
  "userId": "string - User ID"
}
```

**Response Result:**

```json
{
  "eligible": true,
  "lastFeedbackTime": "datetime - Last feedback time (optional)",
  "nextEligibleTime": "datetime - Next eligible feedback time (optional)"
}
```

##### Get Feedback History

**Request Parameters:**

```json
{
  "userId": "string - User ID",
  "pageSize": "int - Page size (optional, default 10)",
  "pageIndex": "int - Page index (optional, default 0)"
}
```

**Response Result:**

```json
{
  "feedbacks": [
    {
      "feedbackId": "string",
      "feedbackType": "string",
      "reason": "string",
      "response": "string",
      "contactRequested": "boolean",
      "email": "string",
      "submittedAt": "datetime"
    }
  ],
  "totalCount": "int - Total count",
  "hasMore": "boolean - Whether there is more data"
}
```

## 3. Core Implementation

### 3.1 Data Structures

#### UserFeedbackState

```csharp
public class UserFeedbackState
{
    /// <summary>
    /// User ID
    /// </summary>
    public string UserId { get; set; }
    
    /// <summary>
    /// Current feedback information
    /// </summary>
    public UserFeedbackInfo CurrentFeedback { get; set; }
    
    /// <summary>
    /// Historical feedback records (archived)
    /// </summary>
    public List<string> ArchivedFeedbacks { get; set; } = new();
    
    /// <summary>
    /// Last feedback time
    /// </summary>
    public DateTime? LastFeedbackTime { get; set; }
    
    /// <summary>
    /// Feedback count
    /// </summary>
    public int FeedbackCount { get; set; }
    
    /// <summary>
    /// Created time
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Updated time
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
```

#### UserFeedbackInfo

```csharp
public class UserFeedbackInfo
{
    /// <summary>
    /// Feedback ID
    /// </summary>
    public string FeedbackId { get; set; }
    
    /// <summary>
    /// Feedback type (Cancel/Change)
    /// </summary>
    public string FeedbackType { get; set; }
    
    /// <summary>
    /// Feedback reason
    /// </summary>
    public string Reason { get; set; }
    
    /// <summary>
    /// User detailed feedback content
    /// </summary>
    public string Response { get; set; }
    
    /// <summary>
    /// Whether contact is requested
    /// </summary>
    public bool ContactRequested { get; set; }
    
    /// <summary>
    /// Contact email
    /// </summary>
    public string Email { get; set; }
    
    /// <summary>
    /// Submission time
    /// </summary>
    public DateTime SubmittedAt { get; set; }
}
```

#### UserFeedbackEventLog

```csharp
public abstract class UserFeedbackEventLog : EventBase
{
    /// <summary>
    /// Event time
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Submit feedback event
/// </summary>
public class SubmitFeedbackLogEvent : UserFeedbackEventLog
{
    public UserFeedbackInfo FeedbackInfo { get; set; }
    public DateTime SubmittedAt { get; set; }
}

/// <summary>
/// Archive feedback event
/// </summary>
public class ArchiveFeedbackLogEvent : UserFeedbackEventLog
{
    public UserFeedbackInfo FeedbackToArchive { get; set; }
    public DateTime ArchiveTime { get; set; }
}

/// <summary>
/// Update feedback eligibility event
/// </summary>
public class UpdateFeedbackEligibilityLogEvent : UserFeedbackEventLog
{
    public DateTime? LastFeedbackTime { get; set; }
    public DateTime UpdateTime { get; set; }
}
```

### 3.2 Core Algorithms

#### Frequency Control Algorithm

```csharp
public bool CanSubmitFeedback(DateTime? lastFeedbackTime)
{
    if (lastFeedbackTime == null)
        return true;
        
    var timeSinceLastFeedback = DateTime.UtcNow - lastFeedbackTime.Value;
    return timeSinceLastFeedback.TotalDays >= 14;
}
```

#### Feedback Data Archiving Algorithm (Event-Driven)

```csharp
public async Task ArchiveCurrentFeedbackAsync()
{
    if (State.CurrentFeedback != null)
    {
        // Archive data through events, not direct state modification
        RaiseEvent(new ArchiveFeedbackLogEvent
        {
            FeedbackToArchive = State.CurrentFeedback,
            ArchiveTime = DateTime.UtcNow
        });
        
        // Confirm events to persist state changes
        await ConfirmEvents();
    }
}
```

### 3.3 Extensibility Design

- **Feedback Type Extension**: Support adding new feedback types through configuration file management
- **Data Export**: Support CSV format data export for data analysis
- **Multi-language Support**: Feedback reasons and prompt messages support internationalization
- **Statistical Analysis**: Reserved statistical analysis interfaces, supporting feedback data aggregation queries

### 3.4 Integration with Existing Code

#### GAgent Interface Definition

```csharp
public interface IUserFeedbackGAgent : IGAgent
{
    /// <summary>
    /// Submit user feedback
    /// </summary>
    Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request);
    
    /// <summary>
    /// Check feedback eligibility
    /// </summary>
    Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync();
    
    /// <summary>
    /// Get feedback history
    /// </summary>
    Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(GetFeedbackHistoryRequest request);
    
    /// <summary>
    /// Get user feedback statistics
    /// </summary>
    Task<UserFeedbackStats> GetFeedbackStatsAsync();
}
```

#### GAgent Implementation Class

```csharp
[Description("User feedback management agent")]
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(UserFeedbackGAgent))]
[Reentrant]
public class UserFeedbackGAgent : GAgentBase<UserFeedbackState, UserFeedbackEventLog>, 
    IUserFeedbackGAgent
{
    private readonly ILogger<UserFeedbackGAgent> _logger;
    
    public UserFeedbackGAgent(ILogger<UserFeedbackGAgent> logger)
    {
        _logger = logger;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User feedback management and collection");
    }
    
    /// <summary>
    /// Event-driven state transition handling
    /// </summary>
    protected sealed override void GAgentTransitionState(UserFeedbackState state,
        StateLogEventBase<UserFeedbackEventLog> @event)
    {
        switch (@event)
        {
            case SubmitFeedbackLogEvent submitEvent:
                state.CurrentFeedback = submitEvent.FeedbackInfo;
                state.LastFeedbackTime = submitEvent.SubmittedAt;
                state.FeedbackCount++;
                state.UpdatedAt = submitEvent.SubmittedAt;
                break;
                
            case ArchiveFeedbackLogEvent archiveEvent:
                if (state.CurrentFeedback != null)
                {
                    var archivedData = JsonSerializer.Serialize(state.CurrentFeedback);
                    state.ArchivedFeedbacks.Add(archivedData);
                    
                    // Limit archived data count, keep latest 50 records
                    if (state.ArchivedFeedbacks.Count > 50)
                    {
                        state.ArchivedFeedbacks.RemoveAt(0);
                    }
                    
                    state.CurrentFeedback = null;
                    state.UpdatedAt = archiveEvent.ArchiveTime;
                }
                break;
                
            case UpdateFeedbackEligibilityLogEvent eligibilityEvent:
                state.LastFeedbackTime = eligibilityEvent.LastFeedbackTime;
                state.UpdatedAt = eligibilityEvent.UpdateTime;
                break;
        }
    }
    
    // Implement interface methods...
}
```

## Update History

| Date | Version | Update Content | Updater |
|---|---|----|-----|
| 2025-09-24 | V1.0 | Initial version | HyperEcho |