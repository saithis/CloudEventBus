namespace Saithis.CloudEventBus.EfCoreOutbox;

public class OutboxMessageSerializationException(string message, string serializedProperties)
    : OutboxException(message)
{
    public string SerializedProperties { get; } = serializedProperties;
}