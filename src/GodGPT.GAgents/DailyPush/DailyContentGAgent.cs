using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Services;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

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

        // ✅ Pre-register common timezone mappings to prevent orphaned grains
        await EnsureCommonTimezoneMappingsAsync();

        // Auto-refresh content if empty or stale (older than 24 hours)
        var needsRefresh = State.Contents.Count == 0 ||
                           (DateTime.UtcNow - State.LastRefresh).TotalHours > 24;

        if (needsRefresh)
        {
            _logger.LogInformation("Content is empty or stale, triggering auto-refresh from local CSV...");
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

    protected override void GAgentTransitionState(DailyContentGAgentState state,
        StateLogEventBase<DailyPushLogEvent> @event)
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
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            
            // 🎯 Check if content has already been selected for this date (same-day cache)
            if (State.DailySelectedContentCache.TryGetValue(dateKey, out var cachedContentIds))
            {
                _logger.LogInformation("🔄 Returning cached content selection for {Date}: [{ContentIds}] (ensuring same-day consistency)", 
                    dateKey, string.Join(", ", cachedContentIds));
                    
                // Return cached content (filter out any inactive contents)
                return cachedContentIds
                    .Select(id => State.Contents.TryGetValue(id, out var content) ? content : null)
                    .Where(c => c != null && c.IsActive)
                    .Cast<DailyNotificationContent>()
                    .ToList();
            }
            
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
                _logger.LogWarning(
                    "Not enough unused contents ({Available} < {Required}), falling back to all active contents",
                    availableContents.Count, count);
                availableContents = activeContents; // Fallback to all
            }
            else
            {
                _logger.LogDebug("Found {Available} unused contents for selection (required: {Required})",
                    availableContents.Count, count);
            }

            // 🎯 Deterministic selection based on date - ensures global consistency
            // Use target date as seed to guarantee same content selection across all timezones
            var dateSeed = targetDate.ToString("yyyyMMdd").GetHashCode();
            var deterministicRandom = new Random(dateSeed);

            var selectedContents = new List<DailyNotificationContent>();
            var actualCount = Math.Min(count, availableContents.Count);

            _logger.LogInformation(
                "🌍 Global content selection for {Date}: Using deterministic seed {Seed} to ensure timezone consistency",
                targetDate.ToString("yyyy-MM-dd"), dateSeed);

            for (int i = 0; i < actualCount; i++)
            {
                var randomIndex = deterministicRandom.Next(availableContents.Count);
                var selected = availableContents[randomIndex];
                selectedContents.Add(selected);
                availableContents.RemoveAt(randomIndex);

                _logger.LogDebug("📝 Selected content {Index}/{Total}: ID={ContentId}, Title='{Title}'",
                    i + 1, actualCount, selected.Id,
                    selected.LocalizedContents.TryGetValue("en", out var enContent) ? enContent.Title : "N/A");
            }

            // 🎯 Cache the selection result for same-day consistency
            var selectedContentIds = selectedContents.Select(c => c.Id).ToList();
            State.DailySelectedContentCache[dateKey] = selectedContentIds;
            
            // 🧹 Clean old cache entries (called from State.MarkContentAsUsed via ContentSelectionEventLog)
            
            // Raise selection event
            RaiseEvent(new ContentSelectionEventLog
            {
                SelectionDate = targetDate,
                SelectedContentIds = selectedContentIds,
                Count = selectedContents.Count
            });

            await ConfirmEvents();
            _logger.LogInformation("✅ Selected and cached {Count} contents for date {Date}: [{ContentIds}]", 
                selectedContents.Count, targetDate.ToString("yyyy-MM-dd"), string.Join(", ", selectedContentIds));
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
            _logger.LogInformation("🔄 Starting content refresh from local CSV file...");

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

            _logger.LogInformation("📥 Loaded {Count} contents from CSV, converting to DailyNotificationContent...",
                csvContents.Count);

            // Convert CSV content to DailyNotificationContent objects
            var convertedContents = new List<DailyNotificationContent>();

            foreach (var csvContent in csvContents)
            {
                try
                {
                    var notificationContent = new DailyNotificationContent
                    {
                        Id = csvContent.ContentKey,
                        IsActive = true,
                        LocalizedContents = new Dictionary<string, LocalizedContentData>()
                    };

                    // Add English content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleEn) || !string.IsNullOrEmpty(csvContent.ContentEn))
                    {
                        notificationContent.LocalizedContents["en"] = new LocalizedContentData
                        {
                            Title = csvContent.TitleEn ?? "",
                            Content = csvContent.ContentEn ?? ""
                        };
                    }

                    // Add Traditional Chinese content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleZh) || !string.IsNullOrEmpty(csvContent.ContentZh))
                    {
                        notificationContent.LocalizedContents["zh-tw"] = new LocalizedContentData
                        {
                            Title = csvContent.TitleZh ?? "",
                            Content = csvContent.ContentZh ?? ""
                        };
                    }

                    // Add Spanish content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleEs) || !string.IsNullOrEmpty(csvContent.ContentEs))
                    {
                        notificationContent.LocalizedContents["es"] = new LocalizedContentData
                        {
                            Title = csvContent.TitleEs ?? "",
                            Content = csvContent.ContentEs ?? ""
                        };
                    }

                    // Add Simplified Chinese content if available
                    if (!string.IsNullOrEmpty(csvContent.TitleZhSc) || !string.IsNullOrEmpty(csvContent.ContentZhSc))
                    {
                        notificationContent.LocalizedContents["zh"] = new LocalizedContentData
                        {
                            Title = csvContent.TitleZhSc ?? "",
                            Content = csvContent.ContentZhSc ?? ""
                        };
                    }

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

            _logger.LogInformation("✅ Content refresh completed: {Count} contents imported from local CSV file",
                convertedContents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Critical error during content refresh from local CSV file");

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

    public async Task RegisterTimezoneGuidMappingAsync(Guid timezoneGuid, string timezoneId)
    {
        if (!State.TimezoneGuidMappings.ContainsKey(timezoneGuid))
        {
            State.TimezoneGuidMappings[timezoneGuid] = timezoneId;
            await ConfirmEvents();
            _logger.LogInformation("Registered timezone GUID mapping: {Guid} -> {TimezoneId}", timezoneGuid,
                timezoneId);
        }
    }

    public async Task<string?> GetTimezoneFromGuidAsync(Guid timezoneGuid)
    {
        return State.TimezoneGuidMappings.TryGetValue(timezoneGuid, out var timezoneId) ? timezoneId : null;
    }

    public async Task<Dictionary<Guid, string>> GetAllTimezoneMappingsAsync()
    {
        return new Dictionary<Guid, string>(State.TimezoneGuidMappings);
    }

    /// <summary>
    /// Pre-register common timezone mappings to prevent orphaned DailyPushCoordinatorGAgent grains
    /// This solves the chicken-egg problem where auto-activated grains can't find their timezone
    /// </summary>
    private async Task EnsureCommonTimezoneMappingsAsync()
    {
        var commonTimezones = new[]
        {
            "UTC",
            "Asia/Shanghai",
            "Asia/Tokyo",
            "Europe/London",
            "Europe/Rome",
            "Europe/Paris",
            "America/New_York",
            "America/Los_Angeles",
            "Australia/Sydney"
        };

        var registeredCount = 0;
        foreach (var timezone in commonTimezones)
        {
            var timezoneGuid = DailyPushConstants.TimezoneToGuid(timezone);
            if (!State.TimezoneGuidMappings.ContainsKey(timezoneGuid))
            {
                State.TimezoneGuidMappings[timezoneGuid] = timezone;
                registeredCount++;
                _logger.LogDebug("Pre-registered timezone mapping: {Guid} -> {TimezoneId}", timezoneGuid, timezone);
            }
        }

        if (registeredCount > 0)
        {
            await ConfirmEvents();
            _logger.LogInformation("✅ Pre-registered {Count} common timezone mappings to prevent orphaned grains",
                registeredCount);
        }
        else
        {
            _logger.LogDebug("🔄 All common timezone mappings already exist ({Count} total mappings)",
                State.TimezoneGuidMappings.Count);
        }
    }
}