using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(
    IMessageSerializer serializer, 
    IMessageSender sender,
    MessageTypeRegistry typeRegistry) : ICloudEventBus
{
    public async Task PublishDirectAsync<TMessage>(
        TMessage message, 
        MessageEnvelope? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        props ??= new MessageEnvelope();
        
        // Auto-populate type from registry if not explicitly set
        if (string.IsNullOrEmpty(props.Type))
        {
            var typeInfo = typeRegistry.GetByClrType<TMessage>();
            if (typeInfo != null)
            {
                props.Type = typeInfo.EventType;
                
                // Copy registered extensions
                foreach (var ext in typeInfo.Extensions)
                {
                    props.TransportMetadata.TryAdd(ext.Key, ext.Value);
                }
            }
            else
            {
                // Try attribute fallback
                props.Type = typeRegistry.ResolveEventType(typeof(TMessage));
            }
        }
        
        var serializedMessage = serializer.Serialize(message, props);
        await sender.SendAsync(serializedMessage, props, cancellationToken);
    }
}