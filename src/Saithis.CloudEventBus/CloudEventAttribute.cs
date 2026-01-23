namespace Saithis.CloudEventBus;

/// <summary>
/// Marks a class as a CloudEvent message with the specified type identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class CloudEventAttribute : Attribute
{
    /// <summary>
    /// The CloudEvent type identifier (e.g., "com.example.order.created").
    /// </summary>
    public string Type { get; }
    
    /// <summary>
    /// Optional source URI for this event type.
    /// </summary>
    public string? Source { get; set; }
    
    public CloudEventAttribute(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Type cannot be null or whitespace", nameof(type));
        Type = type;
    }
}
