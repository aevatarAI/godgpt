using System.Collections.Generic;
using System.Diagnostics;

namespace Aevatar.Application.Grains.Common.Observability;

/// <summary>
/// Interface for recording execution time metrics to OpenTelemetry
/// </summary>
public interface IMetricsRecorder
{
    /// <summary>
    /// Records execution time in milliseconds
    /// </summary>
    /// <param name="metricName">Name of the metric (e.g., "chat", "voice_chat")</param>
    /// <param name="elapsedMilliseconds">Elapsed time in milliseconds</param>
    /// <param name="tags">Optional tags for additional context</param>
    void Record(string metricName, long elapsedMilliseconds, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Records execution time from a Stopwatch
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="stopwatch">The stopwatch containing elapsed time</param>
    /// <param name="tags">Optional tags for additional context</param>
    void Record(string metricName, Stopwatch stopwatch, params KeyValuePair<string, object?>[] tags);
}

