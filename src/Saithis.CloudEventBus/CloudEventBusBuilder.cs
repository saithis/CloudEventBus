using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Config;

namespace Saithis.CloudEventBus;

public class CloudEventBusBuilder
{
    public IServiceCollection Services { get; }
    internal CloudEventsOptions CloudEventsOptions { get; } = new();
    internal ChannelRegistry ChannelRegistry { get; } = new();
    
    internal CloudEventBusBuilder(IServiceCollection services)
    {
        Services = services;
    }
    
    #region New Channel Config

    public CloudEventBusBuilder AddEventPublishChannel(string channelName, Action<ChannelBuilder> configure)
        => AddChannel(channelName, ChannelType.EventPublish, configure);

    public CloudEventBusBuilder AddCommandPublishChannel(string channelName, Action<ChannelBuilder> configure)
        => AddChannel(channelName, ChannelType.CommandPublish, configure);

    public CloudEventBusBuilder AddCommandConsumeChannel(string channelName, Action<ChannelBuilder> configure)
        => AddChannel(channelName, ChannelType.CommandConsume, configure);

    public CloudEventBusBuilder AddEventConsumeChannel(string channelName, Action<ChannelBuilder> configure)
        => AddChannel(channelName, ChannelType.EventConsume, configure);

    private CloudEventBusBuilder AddChannel(string name, ChannelType intent, Action<ChannelBuilder> configure)
    {
        var channel = new ChannelRegistration(name, intent);
        var builder = new ChannelBuilder(channel);
        configure(builder);
        ChannelRegistry.Register(channel);
        return this;
    }

    #endregion

    /// <summary>
    /// Configures CloudEvents format options.
    /// </summary>
    public CloudEventBusBuilder ConfigureCloudEvents(Action<CloudEventsOptions> configure)
    {
        configure(CloudEventsOptions);
        return this;
    }
    
    /// <summary>
    /// Registers a message handler.
    /// </summary>
    public CloudEventBusBuilder AddHandler<TMessage, THandler>()
        where TMessage : notnull
        where THandler : class, IMessageHandler<TMessage>
    {
        Services.AddScoped<THandler>();
        Services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());
        
        return this;
    }
}
