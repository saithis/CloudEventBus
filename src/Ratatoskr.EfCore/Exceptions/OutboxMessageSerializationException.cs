namespace Ratatoskr.EfCore;

public class OutboxMessageSerializationException(string message, string serializedProperties)
    : OutboxException(message)
{
    public string SerializedProperties { get; } = serializedProperties;
}