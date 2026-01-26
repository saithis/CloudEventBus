namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Default implementation that enriches MessageProperties with metadata from the MessageTypeRegistry.
/// </summary>
public class MessagePropertiesEnricher(MessageTypeRegistry typeRegistry) : IMessagePropertiesEnricher
{
    public MessageProperties Enrich<TMessage>(MessageProperties? properties) where TMessage : notnull
    {
        return Enrich(typeof(TMessage), properties);
    }
    
    public MessageProperties Enrich(Type messageType, MessageProperties? properties)
    {
        properties ??= new MessageProperties();
        
        // Set Time if not already set
        properties.Time ??= DateTimeOffset.UtcNow;
        
        // Query registry for type info
        var typeInfo = typeRegistry.GetByClrType(messageType);
        
        if (typeInfo != null)
        {
            // Enrich Type if not already set
            if (string.IsNullOrEmpty(properties.Type))
            {
                properties.Type = typeInfo.EventType;
            }
            
            // Enrich Source if not already set
            if (string.IsNullOrEmpty(properties.Source) && !string.IsNullOrEmpty(typeInfo.Source))
            {
                properties.Source = typeInfo.Source;
            }
            
            // Copy registered extensions to TransportMetadata (only if not already present)
            foreach (var ext in typeInfo.Extensions)
            {
                properties.TransportMetadata.TryAdd(ext.Key, ext.Value);
            }
        }
        else
        {
            // Fall back to attribute-based resolution for Type if not set
            if (string.IsNullOrEmpty(properties.Type))
            {
                var resolvedType = typeRegistry.ResolveEventType(messageType);
                if (resolvedType != null)
                {
                    properties.Type = resolvedType;
                }
            }
        }
        
        return properties;
    }
}
