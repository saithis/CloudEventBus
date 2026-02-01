namespace Ratatoskr.Core;

public class MessageRegistration(Type messageType, string messageTypeName)
{
    public Type MessageType { get; } = messageType;
    public string MessageTypeName { get; internal set; } = messageTypeName;
    public Dictionary<string, object> Metadata { get; } = new();
}
