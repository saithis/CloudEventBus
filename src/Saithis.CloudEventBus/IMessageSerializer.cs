namespace Saithis.CloudEventBus;

public interface IMessageSerializer
{
    byte[] Serialize(object message, MessageProperties messageProperties);
}