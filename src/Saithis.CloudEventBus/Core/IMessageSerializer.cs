namespace Saithis.CloudEventBus.Core;

public interface IMessageSerializer
{
    byte[] Serialize(object message, MessageProperties messageProperties);
}