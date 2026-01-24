using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(
    IMessageSerializer serializer, 
    IMessageSender sender,
    MessageTypeRegistry typeRegistry) : ICloudEventBus
{
    public Task PublishDirectAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return PublishInternalAsync(message, props, cancellationToken);
    }

    [Obsolete("Use PublishDirectAsync to make the non-transactional nature explicit")]
    public Task PublishAsync<TMessage>(
        TMessage message, 
        MessageProperties? props = null, 
        CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        return PublishInternalAsync(message, props, cancellationToken);
    }

    private async Task PublishInternalAsync<TMessage>(
        TMessage message, 
        MessageProperties? props,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        props ??= new MessageProperties();
        
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
                    props.Extensions.TryAdd(ext.Key, ext.Value);
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