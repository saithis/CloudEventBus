namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Deserializes incoming messages to their target types.
/// </summary>
public interface IMessageDeserializer
{
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    object? Deserialize(byte[] body, Type targetType, MessageContext context);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    TMessage? Deserialize<TMessage>(byte[] body, MessageContext context);
}
