using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ratatoskr.Core;

/// <summary>
/// Provides access to the ActivitySource used for OpenTelemetry tracing in Ratatoskr.
/// </summary>
public static class RatatoskrDiagnostics
{
    public const string ActivitySourceName = "Ratatoskr";
    public const string MeterName = "Ratatoskr";

    /// <summary>
    /// The ActivitySource for Ratatoskr.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// The Meter for Ratatoskr metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName);

    // Messaging Metrics (Standard - prefixed with ratatoskr as requested)
    public static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("ratatoskr.publish.duration", "ms", "Measures the duration of message publishing.");
    public static readonly Histogram<double> ProcessDuration = Meter.CreateHistogram<double>("ratatoskr.process.duration", "ms", "Measures the duration of message processing.");
    
    public static readonly Counter<long> PublishMessages = Meter.CreateCounter<long>("ratatoskr.publish.messages", "nodim", "Number of messages published.");
    public static readonly Counter<long> ReceiveMessages = Meter.CreateCounter<long>("ratatoskr.receive.messages", "nodim", "Number of messages received.");
    public static readonly Counter<long> ProcessMessages = Meter.CreateCounter<long>("ratatoskr.process.messages", "nodim", "Number of messages processed.");

    // Latency Metrics
    public static readonly Histogram<double> ReceiveLag = Meter.CreateHistogram<double>("ratatoskr.receive.lag", "ms", "Duration from message creation (sent) to reception.");
    public static readonly Histogram<double> ProcessLag = Meter.CreateHistogram<double>("ratatoskr.process.lag", "ms", "Duration from message creation (sent) to completion of processing.");

    // Reliability Metrics
    public static readonly Counter<long> RetryMessages = Meter.CreateCounter<long>("ratatoskr.retry.messages", "nodim", "Number of messages scheduled for retry.");
    public static readonly Counter<long> DeadLetterMessages = Meter.CreateCounter<long>("ratatoskr.dead_letter.messages", "nodim", "Number of messages sent to DLQ.");

    // Outbox Metrics
    public static readonly Counter<long> OutboxProcessCount = Meter.CreateCounter<long>("ratatoskr.outbox.process.count", "nodim", "Number of messages processed from the outbox.");
    public static readonly Histogram<double> OutboxProcessDuration = Meter.CreateHistogram<double>("ratatoskr.outbox.process.duration", "ms", "Duration of the outbox processing batch.");
    public static readonly Histogram<long> OutboxBatchSize = Meter.CreateHistogram<long>("ratatoskr.outbox.batch.size", "nodim", "Number of messages picked up in a batch.");
}
