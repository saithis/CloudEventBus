using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(
    IMessageSerializer serializer, 
    IMessageSender sender,
    IMessagePropertiesEnricher enricher) : ICloudEventBus
{
    public async Task PublishDirectAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        props = enricher.Enrich<TMessage>(props);
        
        var serializedMessage = serializer.Serialize(message);
        props.ContentType = serializer.ContentType;
        await sender.SendAsync(serializedMessage, props, cancellationToken);
    }
}