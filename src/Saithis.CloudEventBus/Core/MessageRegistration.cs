namespace Saithis.CloudEventBus.Core;

public class MessageRegistration(Type messageType, string messageTypeName)
{
    public Type MessageType { get; } = messageType;
    public string MessageTypeName { get; } = messageTypeName;
    public Dictionary<string, object> Metadata { get; } = new();
}
