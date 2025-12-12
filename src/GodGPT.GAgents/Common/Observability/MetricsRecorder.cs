using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AElf.OpenTelemetry;
using Volo.Abp.DependencyInjection;

namespace Aevatar.Application.Grains.Common.Observability;

/// <summary>
/// Records method execution time to OpenTelemetry Metrics
/// Thread-safe histogram cache for performance optimization
/// </summary>
public class MetricsRecorder : IMetricsRecorder, ISingletonDependency
{
    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, Histogram<long>> _histogramMapCache = new();

    public MetricsRecorder(IInstrumentationProvider instrumentationProvider)
    {
        _meter = instrumentationProvider.Meter;
    }

    /// <summary>
    /// Records execution time in milliseconds with optional tags
    /// </summary>
    /// <param name="metricName">Name of the metric (e.g., "chat", "voice_chat")</param>
    /// <param name="elapsedMilliseconds">Elapsed time in milliseconds</param>
    /// <param name="tags">Optional tags for additional context</param>
    public void Record(string metricName, long elapsedMilliseconds, params KeyValuePair<string, object?>[] tags)
    {
        var histogram = GetHistogram(metricName);
        
        if (tags != null && tags.Length > 0)
        {
            histogram.Record(elapsedMilliseconds, tags);
        }
        else
        {
            histogram.Record(elapsedMilliseconds);
        }
    }

    /// <summary>
    /// Records execution time from a Stopwatch with optional tags
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="stopwatch">The stopwatch containing elapsed time</param>
    /// <param name="tags">Optional tags for additional context</param>
    public void Record(string metricName, Stopwatch stopwatch, params KeyValuePair<string, object?>[] tags)
    {
        Record(metricName, stopwatch.ElapsedMilliseconds, tags);
    }

    /// <summary>
    /// Gets or creates a histogram from cache
    /// </summary>
    private Histogram<long> GetHistogram(string metricName)
    {
        var key = $"{metricName}.execution.time";

        if (_histogramMapCache.TryGetValue(key, out var cachedHistogram))
        {
            return cachedHistogram;
        }

        var histogram = _meter.CreateHistogram<long>(
            name: key,
            description: $"Histogram for {metricName} execution time",
            unit: "ms");

        _histogramMapCache.TryAdd(key, histogram);
        return histogram;
    }
}

