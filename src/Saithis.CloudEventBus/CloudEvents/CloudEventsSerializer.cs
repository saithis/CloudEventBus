using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.CloudEvents;

/// <summary>
/// Wraps another serializer to add CloudEvents envelope.
/// </summary>
public class CloudEventsSerializer : IMessageSerializer
{
    private readonly IMessageSerializer _innerSerializer;
    private readonly CloudEventsOptions _options;
    private readonly MessageTypeRegistry _typeRegistry;
    private readonly TimeProvider _timeProvider;
    
    public CloudEventsSerializer(
        IMessageSerializer innerSerializer,
        CloudEventsOptions options,
        MessageTypeRegistry typeRegistry,
        TimeProvider timeProvider)
    {
        _innerSerializer = innerSerializer;
        _options = options;
        _typeRegistry = typeRegistry;
        _timeProvider = timeProvider;
    }
    
    public byte[] Serialize(object message, MessageProperties props)
    {
        if (!_options.Enabled)
        {
            return _innerSerializer.Serialize(message, props);
        }
        
        // Ensure required fields are set
        props.Id ??= Guid.NewGuid().ToString();
        props.Time ??= _timeProvider.GetUtcNow();
        props.Source ??= ResolveSource(message.GetType()) ?? _options.DefaultSource;
        props.Type ??= _typeRegistry.ResolveEventType(message.GetType());
        
        if (string.IsNullOrEmpty(props.Type))
        {
            throw new InvalidOperationException(
                $"CloudEvents 'type' is required but not set for {message.GetType().Name}. " +
                "Either register the message type or set MessageProperties.Type explicitly.");
        }
        
        return _options.ContentMode switch
        {
            CloudEventsContentMode.Structured => SerializeStructured(message, props),
            CloudEventsContentMode.Binary => SerializeBinary(message, props),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    
    
    public object? Deserialize(byte[] body, Type targetType, MessageContext context)
    {
        // TODO: detect cloud events automatically from the message context (content type header, etc.)
        if (_options.Enabled && 
            _options.ContentMode == CloudEventsContentMode.Structured)
        {
            // Parse CloudEvents envelope and extract data
            var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(body);
            if (envelope?.Data != null)
            {
                // Data is already deserialized as JsonElement, need to convert
                var dataJson = JsonSerializer.Serialize(envelope.Data);
                return JsonSerializer.Deserialize(dataJson, targetType);
            }
            return null;
        }
        
        // Binary mode or CloudEvents disabled - body is the raw data
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageContext context)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), context);
    }
    
    private byte[] SerializeStructured(object message, MessageProperties props)
    {
        // First serialize the data
        var dataBytes = _innerSerializer.Serialize(message, props);
        var dataContentType = props.ContentType;
        
        // Deserialize data to object for embedding in envelope
        // (This is inefficient but keeps the code simple - optimize later if needed)
        object? data = JsonSerializer.Deserialize<object>(dataBytes);
        
        var envelope = new CloudEventEnvelope
        {
            Id = props.Id!,
            Source = props.Source!,
            Type = props.Type!,
            Time = props.Time,
            DataContentType = dataContentType,
            Subject = props.Subject,
            Data = data,
            Extensions = props.CloudEventExtensions.Count > 0 ? props.CloudEventExtensions : null
        };
        
        props.ContentType = CloudEventsConstants.JsonContentType;
        
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }
    
    private byte[] SerializeBinary(object message, MessageProperties props)
    {
        // Serialize data directly
        var bytes = _innerSerializer.Serialize(message, props);
        
        // Add CloudEvents attributes as headers
        props.Headers[CloudEventsConstants.SpecVersionHeader] = CloudEventsConstants.SpecVersion;
        props.Headers[CloudEventsConstants.IdHeader] = props.Id!;
        props.Headers[CloudEventsConstants.SourceHeader] = props.Source!;
        props.Headers[CloudEventsConstants.TypeHeader] = props.Type!;
        
        if (props.Time.HasValue)
        {
            props.Headers[CloudEventsConstants.TimeHeader] = props.Time.Value.ToString("O");
        }
        
        if (!string.IsNullOrEmpty(props.ContentType))
        {
            props.Headers[CloudEventsConstants.DataContentTypeHeader] = props.ContentType;
        }
        
        // Add CloudEvent extensions as headers
        foreach (var ext in props.CloudEventExtensions)
        {
            props.Headers[$"{CloudEventsConstants.HeaderPrefix}{ext.Key}"] = ext.Value.ToString() ?? "";
        }
        
        return bytes;
    }
    
    private string? ResolveSource(Type messageType)
    {
        var typeInfo = _typeRegistry.GetByClrType(messageType);
        return typeInfo?.Source;
    }
}
