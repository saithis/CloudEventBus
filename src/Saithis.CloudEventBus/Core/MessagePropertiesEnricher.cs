using System.Reflection;
using Saithis.CloudEventBus.CloudEvents;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Default implementation that enriches MessageProperties with metadata from the ChannelRegistry.
/// </summary>
public class MessagePropertiesEnricher(ChannelRegistry registry, CloudEventsOptions options) : IMessagePropertiesEnricher
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
        ChannelRegistration? publishChannel = registry.FindPublishChannelForMessage(messageType);
        MessageRegistration? typeInfo = publishChannel?.Messages.FirstOrDefault(m => m.MessageType == messageType);
        if (typeInfo != null)
        {
            // Enrich Type if not already set
            if (string.IsNullOrEmpty(properties.Type))
            {
                properties.Type = typeInfo.MessageTypeName;
            }
            
            // Enrich Source if not already set
            if (string.IsNullOrEmpty(properties.Source) && !string.IsNullOrEmpty(options.DefaultSource))
            {
                properties.Source = options.DefaultSource;
            }
            
            // Copy registered extensions to TransportMetadata (only if not already present)
            foreach (var ext in typeInfo.Metadata)
            {
                 properties.TransportMetadata.TryAdd(ext.Key, ext.Value?.ToString());
            }
        }
        else
        {
             // Fallback: If not registered, try to resolve type from attribute
             if (string.IsNullOrEmpty(properties.Type))
             {
                 var cloudEventAttr = messageType.GetCustomAttribute<Saithis.CloudEventBus.CloudEventAttribute>();
                 if (cloudEventAttr != null)
                 {
                     properties.Type = cloudEventAttr.Type;
                     if (string.IsNullOrEmpty(properties.Source) && cloudEventAttr.Source != null)
                     {
                          properties.Source = cloudEventAttr.Source;
                     }
                 }
                 else
                 {
                     properties.Type = messageType.Name;
                 }
                 
                 if (string.IsNullOrEmpty(properties.Source) && !string.IsNullOrEmpty(options.DefaultSource))
                 {
                     properties.Source = options.DefaultSource;
                 }
             }
        }
        
        return properties;
    }
}
