namespace Saithis.CloudEventBus.Core;

public interface IMessageSerializer
{
    /// <summary>
    /// Serializes a message.
    /// </summary>
    byte[] Serialize(object message, MessageProperties messageProperties);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    object? Deserialize(byte[] body, Type targetType, MessageContext context);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    TMessage? Deserialize<TMessage>(byte[] body, MessageContext context);
}