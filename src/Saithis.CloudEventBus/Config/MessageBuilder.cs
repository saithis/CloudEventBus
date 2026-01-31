using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Config;

public class MessageBuilder(MessageRegistration message)
{
    /// <summary>
    /// Adds or updates metadata for this message registration.
    /// Transport extensions (e.g. RabbitMq) use this to store routing keys etc.
    /// </summary>
    public MessageBuilder WithMetadata(string key, object value)
    {
        message.Metadata[key] = value;
        return this;
    }
}
