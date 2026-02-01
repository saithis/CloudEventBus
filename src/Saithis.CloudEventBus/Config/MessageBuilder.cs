using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Config;

public class MessageBuilder(MessageRegistration message)
{
    internal MessageRegistration MessageRegistration => message;

    public MessageBuilder WithType(string typeName)
    {
        message.MessageTypeName = typeName;
        return this;
    }
}
