using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;
using GodGPT.GAgents.DailyPush.SEvents;
using GodGPT.GAgents.DailyPush.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily content selection and management GAgent implementation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(DailyContentGAgent))]
public class DailyContentGAgent : GAgentBase<DailyContentGAgentState, DailyPushLogEvent>, IDailyContentGAgent
{
    private readonly ILogger<DailyContentGAgent> _logger;
    private readonly Random _random;
    
    public DailyContentGAgent(ILogger<DailyContentGAgent> logger)
    {
        _logger = logger;
        _random = new Random();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Daily content selection and management");
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DailyContentGAgent activated");
        
        // Auto-refresh content if empty or stale (older than 24 hours)
        var needsRefresh = State.Contents.Count == 0 || 
                          (DateTime.UtcNow - State.LastRefresh).TotalHours > 24;
        
        if (needsRefresh)
        {
            _logger.LogInformation("📋 Content is empty or stale, triggering auto-refresh from S3 CSV...");
            try
            {
                await RefreshContentsFromSourceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "⚠️ Auto-refresh failed during activation, will continue with existing content");
            }
        }
        else
        {
            _logger.LogInformation("📚 Content cache is valid: {Count} entries, last refresh: {LastRefresh}", 
                State.Contents.Count, State.LastRefresh);
        }
    }

    protected override void GAgentTransitionState(DailyContentGAgentState state, StateLogEventBase<DailyPushLogEvent> @event)
    {
        switch (@event)
        {
            case AddContentEventLog addEvent:
                state.Contents[addEvent.Content.Id] = addEvent.Content;
                state.LastRefresh = DateTime.UtcNow;
                break;
                
            case UpdateContentEventLog updateEvent:
                if (state.Contents.ContainsKey(updateEvent.ContentId))
                {
                    state.Contents[updateEvent.ContentId] = updateEvent.Content;
                    state.LastRefresh = DateTime.UtcNow;
                }
                break;
                
            case RemoveContentEventLog removeEvent:
                state.Contents.Remove(removeEvent.ContentId);
                state.LastRefresh = DateTime.UtcNow;
                break;
                
            case ContentSelectionEventLog selectionEvent:
                state.LastSelection = selectionEvent.SelectionDate;
                state.SelectionCount++;
                // Mark contents as used for the date
                foreach (var contentId in selectionEvent.SelectedContentIds)
                {
                    state.MarkContentAsUsed(selectionEvent.SelectionDate, contentId);
                }
                break;
                
            case ImportContentsEventLog importEvent:
                foreach (var content in importEvent.Contents)
                {
                    state.Contents[content.Id] = content;
                }
                state.LastRefresh = importEvent.ImportTime;
                break;
                
            case RefreshContentsEventLog refreshEvent:
                state.LastRefresh = refreshEvent.RefreshTime;
                break;
                
            default:
                _logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    public async Task<List<DailyNotificationContent>> GetSmartSelectedContentsAsync(int count, DateTime targetDate)
    {
        try
        {
            var activeContents = State.Contents.Values.Where(c => c.IsActive).ToList();
            if (activeContents.Count == 0)
            {
                _logger.LogWarning("No active contents available for selection on {Date}", targetDate);
                return new List<DailyNotificationContent>();
            }
            
            _logger.LogDebug("Found {Count} active contents for selection on {Date}", 
                activeContents.Count, targetDate);
        
        // Get used content from history
        var usedContentIds = new HashSet<string>();
        for (int i = 0; i < DailyPushConstants.CONTENT_HISTORY_DAYS; i++)
        {
            var checkDate = targetDate.AddDays(-i);
            var dailyUsed = State.GetUsedContentIds(checkDate);
            foreach (var id in dailyUsed)
            {
                usedContentIds.Add(id);
            }
        }
        
            // Filter out recently used contents
            var availableContents = activeContents.Where(c => !usedContentIds.Contains(c.Id)).ToList();
            if (availableContents.Count < count)
            {
                _logger.LogWarning("Not enough unused contents ({Available} < {Required}), falling back to all active contents", 
                    availableContents.Count, count);
                availableContents = activeContents; // Fallback to all
            }
            else
            {
                _logger.LogDebug("Found {Available} unused contents for selection (required: {Required})", 
                    availableContents.Count, count);
            }
        
        // Randomly select
        var selectedContents = new List<DailyNotificationContent>();
        var actualCount = Math.Min(count, availableContents.Count);
        
        for (int i = 0; i < actualCount; i++)
        {
            var randomIndex = _random.Next(availableContents.Count);
            var selected = availableContents[randomIndex];
            selectedContents.Add(selected);
            availableContents.RemoveAt(randomIndex);
        }
        
        // Raise selection event
        RaiseEvent(new ContentSelectionEventLog
        {
            SelectionDate = targetDate,
            SelectedContentIds = selectedContents.Select(c => c.Id).ToList(),
            Count = selectedContents.Count
        });
        
            await ConfirmEvents();
            _logger.LogInformation($"Selected {selectedContents.Count} contents for date {targetDate:yyyy-MM-dd}");
            return selectedContents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select contents for date {Date}", targetDate);
            return new List<DailyNotificationContent>();
        }
    }

    public async Task AddContentAsync(DailyNotificationContent content)
    {
        RaiseEvent(new AddContentEventLog
        {
            Content = content
        });
        
        await ConfirmEvents();
        _logger.LogInformation($"Added content: {content.Id}");
    }

    public async Task UpdateContentAsync(string contentId, DailyNotificationContent content)
    {
        if (State.Contents.ContainsKey(contentId))
        {
            RaiseEvent(new UpdateContentEventLog
            {
                ContentId = contentId,
                Content = content
            });
            
            await ConfirmEvents();
            _logger.LogInformation($"Updated content: {contentId}");
        }
    }

    public async Task<List<DailyNotificationContent>> GetAllContentsAsync()
    {
        return State.Contents.Values.ToList();
    }

    public async Task<DailyNotificationContent?> GetContentByIdAsync(string contentId)
    {
        return State.Contents.TryGetValue(contentId, out var content) ? content : null;
    }

    public async Task<bool> RemoveContentAsync(string contentId)
    {
        if (State.Contents.ContainsKey(contentId))
        {
            RaiseEvent(new RemoveContentEventLog
            {
                ContentId = contentId
            });
            
            await ConfirmEvents();
            _logger.LogInformation($"Removed content: {contentId}");
            return true;
        }
        return false;
    }

    public async Task<int> GetActiveContentCountAsync()
    {
        return State.Contents.Values.Count(c => c.IsActive);
    }

    public async Task RefreshContentsFromSourceAsync()
    {
        try
        {
            _logger.LogInformation("🔄 Starting content refresh from S3 CSV source...");
            
            // Get DailyPushContentService from service provider
            var contentService = ServiceProvider.GetService<DailyPushContentService>();
            if (contentService == null)
            {
                _logger.LogError("❌ DailyPushContentService not available, cannot refresh contents");
                return;
            }
            
            // Load all available content from CSV
            var csvContents = await contentService.GetAllContentsAsync();
            if (csvContents.Count == 0)
            {
                _logger.LogWarning("⚠️ No contents loaded from CSV source");
                return;
            }
            
            _logger.LogInformation("📥 Loaded {Count} contents from CSV, converting to DailyNotificationContent...", csvContents.Count);
            
            // Convert CSV content to DailyNotificationContent objects
            var convertedContents = new List<DailyNotificationContent>();
            
            foreach (var csvContent in csvContents)
            {
                try
                {
                    var notificationContent = new DailyNotificationContent
                    {
                        Id = csvContent.ContentKey,
                        TitleEn = csvContent.TitleEn,
                        ContentEn = csvContent.ContentEn,
                        TitleZh = csvContent.TitleZh,
                        ContentZh = csvContent.ContentZh,
                        TitleEs = csvContent.TitleEs,
                        ContentEs = csvContent.ContentEs,
                        TitleZhSc = csvContent.TitleZhSc,
                        ContentZhSc = csvContent.ContentZhSc,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        LastUsed = DateTime.MinValue
                    };
                    
                    convertedContents.Add(notificationContent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to convert CSV content {ContentKey}", csvContent.ContentKey);
                }
            }
            
            if (convertedContents.Count == 0)
            {
                _logger.LogError("❌ No valid contents could be converted from CSV");
                return;
            }
            
            // Import all converted contents
            RaiseEvent(new ImportContentsEventLog
            {
                Contents = convertedContents,
                ImportTime = DateTime.UtcNow
            });
            
            // Mark refresh completed
            RaiseEvent(new RefreshContentsEventLog
            {
                RefreshTime = DateTime.UtcNow
            });
            
            await ConfirmEvents();
            
            _logger.LogInformation("✅ Content refresh completed: {Count} contents imported from S3 CSV source", 
                convertedContents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Critical error during content refresh from S3 CSV source");
            
            // Still mark refresh time even if failed
            RaiseEvent(new RefreshContentsEventLog
            {
                RefreshTime = DateTime.UtcNow
            });
            
            await ConfirmEvents();
        }
    }

    public async Task ImportContentsAsync(List<DailyNotificationContent> contents)
    {
        RaiseEvent(new ImportContentsEventLog
        {
            Contents = contents,
            ImportTime = DateTime.UtcNow
        });
        
        await ConfirmEvents();
        _logger.LogInformation($"Imported {contents.Count} contents");
    }

    public async Task<ContentStatistics> GetStatisticsAsync()
    {
        var activeContents = State.Contents.Values.Where(c => c.IsActive).ToList();
        
        return new ContentStatistics
        {
            TotalContents = State.Contents.Count,
            ActiveContents = activeContents.Count,
            LanguageDistribution = activeContents
                .SelectMany(c => c.LocalizedContents.Keys)
                .GroupBy(lang => lang)
                .ToDictionary(g => g.Key, g => g.Count()),
            LastSelection = State.LastSelection,
            TotalSelections = State.SelectionCount
        };
    }
}
