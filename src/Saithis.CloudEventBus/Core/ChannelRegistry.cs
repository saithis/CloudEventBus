using System.Collections.Concurrent;

namespace Saithis.CloudEventBus.Core;

public class ChannelRegistry
{
    private readonly ConcurrentDictionary<string, ChannelRegistration> _publishChannels = new();
    private readonly ConcurrentDictionary<string, ChannelRegistration> _consumeChannels = new();
    private bool _frozen;

    public void Register(ChannelRegistration channel)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Registry is frozen and cannot be modified.");
        }

        var registry = channel.Intent is ChannelType.EventPublish or ChannelType.CommandPublish 
            ? _publishChannels : _consumeChannels;
        if (!registry.TryAdd(channel.ChannelName, channel))
        {
             // For now, simple overwrite or merge logic might be needed if multiple calls use same channel name?
             // But usually unique channel name is expected per purpose.
             // If we allow multiple AddEventPublishChannel with same name, we should probably merge.
             // But simplistic implementation first:
             throw new InvalidOperationException($"Channel '{channel.ChannelName}' is already registered.");
        }
    }

    public ChannelRegistration? GetPublishChannel(string channelName)
    {
        _publishChannels.TryGetValue(channelName, out var channel);
        return channel;
    }

    public ChannelRegistration? GetConsumeChannel(string channelName)
    {
        _consumeChannels.TryGetValue(channelName, out var channel);
        return channel;
    }
    
    public IEnumerable<ChannelRegistration> GetConsumeChannels() => _consumeChannels.Values;
    public IEnumerable<ChannelRegistration> GetAllChannels() => _consumeChannels.Values.Concat(_publishChannels.Values);

    public void Freeze()
    {
        _frozen = true;
    }
    
    // Helper to find which channel a message type is published to
    public ChannelRegistration? FindPublishChannelForMessage(Type messageType)
    {
        // A message type should ideally belong to only one Publish channel
        return _publishChannels.Values
            .FirstOrDefault(c => 
                (c.Intent == ChannelType.EventPublish || c.Intent == ChannelType.CommandPublish) &&
                c.Messages.Any(m => m.MessageType == messageType));
    }

    public ChannelRegistration? FindPublishChannelForTypeName(string messageTypeName)
    {
        return _publishChannels.Values
            .FirstOrDefault(c => 
                (c.Intent == ChannelType.EventPublish || c.Intent == ChannelType.CommandPublish) &&
                c.Messages.Any(m => m.MessageTypeName == messageTypeName));
    }
    
    // Helper to find consumer channels for a wire type name
    public IEnumerable<(ChannelRegistration Channel, MessageRegistration Message)> FindConsumeChannelsForType(string typeName)
    {
        foreach (var channel in _publishChannels.Values)
        {
             if (channel.Intent != ChannelType.EventConsume && channel.Intent != ChannelType.CommandConsume)
                 continue;

             var msg = channel.Messages.FirstOrDefault(m => m.MessageTypeName == typeName);
             if (msg != null)
             {
                 yield return (channel, msg);
             }
        }
    }

    public PublishInformation? GetPublishInformation(Type messageType)
    {
        var channel = FindPublishChannelForMessage(messageType);
        var message = channel?.GetMessage(messageType);
        if (channel == null || message == null) return null;
        return new PublishInformation
        {
            Channel =  channel,
            Message = message,
        };
    }
}


public class PublishInformation
{
    public required ChannelRegistration Channel { get; init; }
    public required MessageRegistration Message { get; init; }
}