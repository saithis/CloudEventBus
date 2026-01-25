namespace Saithis.CloudEventBus.Core;

public interface IMessageSender
{
    Task SendAsync(byte[] content, MessageEnvelope props, CancellationToken cancellationToken);
}