namespace Ratatoskr.RabbitMq;

/// <summary>
/// Constants for CloudEvents AMQP protocol binding.
/// See: https://github.com/cloudevents/spec/blob/main/cloudevents/bindings/amqp-protocol-binding.md
/// </summary>
public static class CloudEventsAmqpConstants
{
    /// <summary>
    /// CloudEvents specification version.
    /// </summary>
    public const string SpecVersion = "1.0";
    
    /// <summary>
    /// Content type for structured mode.
    /// </summary>
    public const string JsonContentType = "application/cloudevents+json";
    
    /// <summary>
    /// Header prefix for CloudEvents attributes in binary content mode.
    /// Uses underscore separator (JMS 2.0 compatible, preferred by Wolverine).
    /// </summary>
    public const string HeaderPrefix = "cloudEvents_";
    
    // CloudEvents attribute header names (with underscore separator)
    public const string SpecVersionHeader = "cloudEvents_specversion";
    public const string IdHeader = "cloudEvents_id";
    public const string SourceHeader = "cloudEvents_source";
    public const string TypeHeader = "cloudEvents_type";
    public const string TimeHeader = "cloudEvents_time";
    public const string SubjectHeader = "cloudEvents_subject";
    
    /// <summary>
    /// Alternative header prefix using colon separator.
    /// Supported for incoming messages (per AMQP binding spec).
    /// </summary>
    public const string AlternativeHeaderPrefix = "cloudEvents:";
    
    /// <summary>
    /// Trace propagation header (W3C Trace Context).
    /// </summary>
    public const string TraceParentHeader = "traceparent";
}
