namespace Saithis.CloudEventBus;

public interface IMessageSender
{
    Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken);
}