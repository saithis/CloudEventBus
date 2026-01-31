namespace Saithis.CloudEventBus.Configuration;

/// <summary>
/// Represents a registration for consuming a message.
/// </summary>
public class ConsumptionRegistration
{
    public Type MessageType { get; }
    public MessageIntent Intent { get; }
    public string ChannelName { get; } // Exchange
    public string SubscriptionName { get; } // Queue
    public Dictionary<string, object> Metadata { get; } = new();

    public ConsumptionRegistration(Type messageType, MessageIntent intent, string channelName, string subscriptionName)
    {
        MessageType = messageType;
        Intent = intent;
        ChannelName = channelName;
        SubscriptionName = subscriptionName;
    }
}
