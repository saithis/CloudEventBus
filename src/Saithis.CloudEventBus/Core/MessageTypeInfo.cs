namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Information about a registered message type.
/// </summary>
public class MessageTypeInfo(
    Type clrType,
    string eventType,
    string? source = null,
    IReadOnlyDictionary<string, string>? extensions = null)
{
    public Type ClrType { get; } = clrType;
    public string EventType { get; } = eventType;
    public string? Source { get; } = source;
    public IReadOnlyDictionary<string, string> Extensions { get; } = extensions ?? new Dictionary<string, string>();
}

/// <summary>
/// Builder for configuring a message type registration.
/// </summary>
public class MessageTypeBuilder
{
    private readonly Type _clrType;
    private readonly string _eventType;
    private string? _source;
    private readonly Dictionary<string, string> _extensions = new();
    
    internal MessageTypeBuilder(Type clrType, string eventType)
    {
        _clrType = clrType;
        _eventType = eventType;
    }
    
    /// <summary>
    /// Sets the CloudEvent source for this message type.
    /// </summary>
    public MessageTypeBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }
    
    /// <summary>
    /// Adds a custom extension that will be included with every message of this type.
    /// </summary>
    public MessageTypeBuilder WithExtension(string key, string value)
    {
        // TODO: this gets mapped into TransportMetadata, which doesn't make sense. Make 3 methods: WithTransportMetadata, WithCloudEventsProperty, WithHeader and map them all correctly 
        _extensions[key] = value;
        return this;
    }
    
    internal MessageTypeInfo Build()
    {
        return new MessageTypeInfo(_clrType, _eventType, _source, _extensions);
    }
}
