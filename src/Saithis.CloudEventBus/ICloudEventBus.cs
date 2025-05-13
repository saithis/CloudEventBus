using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public interface ICloudEventBus
{
    Task PublishAsync<TMessage>(TMessage message, MessageProperties? props = null, CancellationToken cancellationToken = default)
        where TMessage : notnull;
}