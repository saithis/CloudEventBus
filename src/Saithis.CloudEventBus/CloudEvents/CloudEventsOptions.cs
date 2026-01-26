namespace Saithis.CloudEventBus.CloudEvents;

public class CloudEventsOptions
{
    /// <summary>
    /// Whether to wrap messages in CloudEvents envelope. Default true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Default source URI if not specified per message or message type.
    /// </summary>
    public string DefaultSource { get; set; } = "/";
}
