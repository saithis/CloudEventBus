using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

/// <summary>
/// Maps between MessageProperties and RabbitMQ message properties.
/// Handles CloudEvents protocol binding for AMQP.
/// </summary>
public interface IRabbitMqEnvelopeMapper
{
    /// <summary>
    /// Maps outgoing message to RabbitMQ properties.
    /// Returns the body to send (may be wrapped for structured mode).
    /// </summary>
    /// <param name="serializedData">The serialized message data.</param>
    /// <param name="props">Message properties to map.</param>
    /// <param name="outgoing">RabbitMQ properties to populate.</param>
    /// <returns>The message body to send (may be wrapped in CloudEvents envelope).</returns>
    byte[] MapOutgoing(byte[] serializedData, MessageProperties props, BasicProperties outgoing);
    
    /// <summary>
    /// Maps incoming RabbitMQ message to MessageProperties.
    /// Returns the body to deserialize (extracted from envelope for structured mode).
    /// </summary>
    /// <param name="incoming">The incoming RabbitMQ message.</param>
    /// <returns>A tuple containing the body to deserialize and the mapped properties.</returns>
    (byte[] body, MessageProperties props) MapIncoming(BasicDeliverEventArgs incoming);
}
