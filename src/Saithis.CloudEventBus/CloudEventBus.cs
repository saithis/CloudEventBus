using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBus(IMessageSerializer serializer, IMessageSender sender) : ICloudEventBus
{
    public async Task PublishAsync<TMessage>(TMessage message, MessageProperties? props = null, CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        props ??= new MessageProperties();
        var serializedMessage = serializer.Serialize(message, props);
        await sender.SendAsync(serializedMessage, props, cancellationToken);
    }
}