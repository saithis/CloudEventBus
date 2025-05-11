namespace Saithis.CloudEventBus.EfCoreOutbox;

public class MessagePropertyDeserializationOutboxException(string message, string propertiesJson)
    : OutboxException(message)
{
    public string PropertiesJson { get; } = propertiesJson;
}