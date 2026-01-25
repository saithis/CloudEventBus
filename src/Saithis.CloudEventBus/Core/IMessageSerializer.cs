namespace Saithis.CloudEventBus.Core;

public interface IMessageSerializer
{
    /// <summary>
    /// Serializes a message.
    /// </summary>
    byte[] Serialize(object message, MessageEnvelope envelope);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    object? Deserialize(byte[] body, Type targetType, MessageEnvelope envelope);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    TMessage? Deserialize<TMessage>(byte[] body, MessageEnvelope envelope);
}