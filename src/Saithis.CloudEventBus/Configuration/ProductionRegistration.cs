namespace Saithis.CloudEventBus.Configuration;

/// <summary>
/// Represents a registration for producing or sending a message.
/// </summary>
public class ProductionRegistration
{
    public Type MessageType { get; }
    public MessageIntent Intent { get; }
    public string ChannelName { get; }
    public Dictionary<string, object> Metadata { get; } = new();

    public ProductionRegistration(Type messageType, MessageIntent intent, string channelName)
    {
        MessageType = messageType;
        Intent = intent;
        ChannelName = channelName;
    }
}
