using Saithis.CloudEventBus.CloudEvents;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Default implementation that enriches MessageProperties with metadata from the ChannelRegistry.
/// </summary>
public class MessagePropertiesEnricher(ChannelRegistry registry, CloudEventsOptions options, TimeProvider timeProvider, ITransportMessageMetadataEnricher transportEnricher) : IMessagePropertiesEnricher
{
    public MessageProperties Enrich<TMessage>(MessageProperties? properties) where TMessage : notnull
    {
        return Enrich(typeof(TMessage), properties);
    }
    
    public MessageProperties Enrich(Type messageType, MessageProperties? properties)
    {
        properties ??= new MessageProperties();
        
        properties.Id ??= Guid.NewGuid().ToString();
        properties.Time ??= timeProvider.GetUtcNow();
        properties.Source ??= options.DefaultSource;
        
        // Query registry for type info
        var publishInfo = registry.GetPublishInformation(messageType);
        if (publishInfo == null) 
            return properties;
        
        // Enrich Type if not already set
        if (string.IsNullOrEmpty(properties.Type))
        {
            properties.Type = publishInfo.Message.MessageTypeName;
        }
            
        transportEnricher.Enrich(publishInfo, properties);

        return properties;
    }
}
