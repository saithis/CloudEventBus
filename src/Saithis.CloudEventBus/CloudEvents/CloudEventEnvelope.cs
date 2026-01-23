using System.Text.Json.Serialization;

namespace Saithis.CloudEventBus.CloudEvents;

/// <summary>
/// CloudEvents envelope in structured content mode.
/// </summary>
public class CloudEventEnvelope
{
    [JsonPropertyName("specversion")]
    public string SpecVersion { get; init; } = CloudEventsConstants.SpecVersion;
    
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    [JsonPropertyName("source")]
    public required string Source { get; init; }
    
    [JsonPropertyName("type")]
    public required string Type { get; init; }
    
    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; init; }
    
    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; init; }
    
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }
    
    /// <summary>
    /// The event data (payload).
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; init; }
    
    /// <summary>
    /// Extension attributes (custom fields).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? Extensions { get; init; }
}
