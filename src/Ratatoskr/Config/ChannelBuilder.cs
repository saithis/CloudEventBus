using System.Reflection;
using Ratatoskr.Core;

namespace Ratatoskr.Config;

public class ChannelBuilder(ChannelRegistration channel)
{
    internal ChannelRegistration Channel => channel;
    
    /// <summary>
    /// Configures transport-specific options for this channel.
    /// </summary>
    public ChannelBuilder WithMetadata(string key, object value)
    {
        channel.Metadata[key] = value;
        return this;
    }

    public ChannelRegistration Build() => channel;

    #region Producers (EventPublish / CommandPublish)

    /// <summary>
    /// Registers a message type that is produced to this channel.
    /// Used for EventPublish and CommandPublish channels.
    /// </summary>
    public ChannelBuilder Produces<T>(Action<MessageBuilder>? configure = null)
    {
        ValidateIntent(ChannelType.EventPublish, ChannelType.CommandPublish);
        AddMessage<T>(configure);
        return this;
    }

    /// <summary>
    /// Alias for Produces, typically used for CommandPublish channels.
    /// </summary>
    public ChannelBuilder Sends<T>(Action<MessageBuilder>? configure = null)
    {
        ValidateIntent(ChannelType.CommandPublish);
        AddMessage<T>(configure);
        return this;
    }

    #endregion

    #region Consumers (CommandConsume / EventConsume)

    /// <summary>
    /// Registers a message type that is consumed from this channel.
    /// Used for CommandConsume and EventConsume channels.
    /// </summary>
    public ChannelBuilder Consumes<T>(Action<MessageBuilder>? configure = null)
    {
        ValidateIntent(ChannelType.CommandConsume, ChannelType.EventConsume);
        AddMessage<T>(configure);
        return this;
    }
    
    #endregion

    private void AddMessage<T>(Action<MessageBuilder>? configure, string? typeName = null)
    {
        var type = typeof(T);
        typeName ??= GetMessageTypeName(type);
        
        var registration = new MessageRegistration(type, typeName);
        
        if (configure != null)
        {
            var builder = new MessageBuilder(registration);
            configure(builder);
        }

        channel.Messages.Add(registration);
    }

    private void ValidateIntent(params ChannelType[] allowed)
    {
        if (!allowed.Contains(channel.Intent))
        {
            throw new InvalidOperationException(
                $"This method cannot be called on a channel with intent '{channel.Intent}'. " +
                $"Allowed intents: {string.Join(", ", allowed)}");
        }
    }

    private static string GetMessageTypeName(Type type)
    {
        var attr = type.GetCustomAttribute<RatatoskrMessageAttribute>();
        return attr?.Type ?? type.Name;
    }
}
