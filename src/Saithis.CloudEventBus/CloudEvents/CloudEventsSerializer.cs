using System.Text.Json;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.CloudEvents;

/// <summary>
/// Wraps another serializer to add CloudEvents envelope.
/// </summary>
public class CloudEventsSerializer(
    IMessageSerializer innerSerializer,
    CloudEventsOptions options,
    MessageTypeRegistry typeRegistry,
    TimeProvider timeProvider)
    : IMessageSerializer
{
    public byte[] Serialize(object message, MessageEnvelope props)
    {
        if (!options.Enabled)
        {
            return innerSerializer.Serialize(message, props);
        }
        
        // Ensure required fields are set
        props.Id ??= Guid.NewGuid().ToString();
        props.Time ??= timeProvider.GetUtcNow();
        props.Source ??= ResolveSource(message.GetType()) ?? options.DefaultSource;
        props.Type ??= typeRegistry.ResolveEventType(message.GetType());
        
        if (string.IsNullOrEmpty(props.Type))
        {
            throw new InvalidOperationException(
                $"CloudEvents 'type' is required but not set for {message.GetType().Name}. " +
                "Either register the message type or set MessageProperties.Type explicitly.");
        }
        
        return options.ContentMode switch
        {
            CloudEventsContentMode.Structured => SerializeStructured(message, props),
            CloudEventsContentMode.Binary => SerializeBinary(message, props),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    
    
    public object? Deserialize(byte[] body, Type targetType, MessageEnvelope envelope)
    {
        // TODO: detect cloud events automatically from the message context (content type header, etc.)
        if (options.Enabled && 
            options.ContentMode == CloudEventsContentMode.Structured)
        {
            // Parse CloudEvents envelope and extract data
            var cloudEvent = JsonSerializer.Deserialize<CloudEventEnvelope>(body);
            if (cloudEvent?.Data != null)
            {
                // Data is already deserialized as JsonElement, need to convert
                var dataJson = JsonSerializer.Serialize(cloudEvent.Data);
                return JsonSerializer.Deserialize(dataJson, targetType);
            }
            return null;
        }
        
        // Binary mode or CloudEvents disabled - body is the raw data
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageEnvelope envelope)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), envelope);
    }
    
    private byte[] SerializeStructured(object message, MessageEnvelope envelope)
    {
        // First serialize the data
        var dataBytes = innerSerializer.Serialize(message, envelope);
        var dataContentType = envelope.ContentType;
        
        // Deserialize data to object for embedding in envelope
        // (This is inefficient but keeps the code simple - optimize later if needed)
        object? data = JsonSerializer.Deserialize<object>(dataBytes);
        
        var cloudEvent = new CloudEventEnvelope
        {
            Id = envelope.Id!,
            Source = envelope.Source!,
            Type = envelope.Type!,
            Time = envelope.Time,
            DataContentType = dataContentType,
            Subject = envelope.Subject,
            Data = data,
            Extensions = envelope.CloudEventExtensions.Count > 0 ? envelope.CloudEventExtensions : null
        };
        
        envelope.ContentType = CloudEventsConstants.JsonContentType;
        
        return JsonSerializer.SerializeToUtf8Bytes(cloudEvent);
    }
    
    private byte[] SerializeBinary(object message, MessageEnvelope props)
    {
        // Serialize data directly
        var bytes = innerSerializer.Serialize(message, props);
        
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
        var typeInfo = typeRegistry.GetByClrType(messageType);
        return typeInfo?.Source;
    }
}
