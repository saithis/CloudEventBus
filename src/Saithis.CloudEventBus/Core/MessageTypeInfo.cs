namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Information about a registered message type.
/// </summary>
public class MessageTypeInfo
{
    public Type ClrType { get; }
    public string EventType { get; }
    public string? Source { get; }
    public IReadOnlyDictionary<string, string> Extensions { get; }
    
    public MessageTypeInfo(
        Type clrType, 
        string eventType, 
        string? source = null,
        IReadOnlyDictionary<string, string>? extensions = null)
    {
        ClrType = clrType;
        EventType = eventType;
        Source = source;
        Extensions = extensions ?? new Dictionary<string, string>();
    }
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
        _extensions[key] = value;
        return this;
    }
    
    internal MessageTypeInfo Build()
    {
        return new MessageTypeInfo(_clrType, _eventType, _source, _extensions);
    }
}
