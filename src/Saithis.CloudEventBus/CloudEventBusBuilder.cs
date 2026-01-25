using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBusBuilder
{
    internal IServiceCollection Services { get; }
    internal MessageTypeRegistry TypeRegistry { get; } = new();
    internal CloudEventsOptions CloudEventsOptions { get; } = new();
    internal MessageHandlerRegistry HandlerRegistry { get; } = new();
    
    internal CloudEventBusBuilder(IServiceCollection services)
    {
        Services = services;
    }
    
    /// <summary>
    /// Registers a message type with the specified event type.
    /// </summary>
    public CloudEventBusBuilder AddMessage<TMessage>(string eventType, Action<MessageTypeBuilder>? configure = null)
        where TMessage : class
    {
        TypeRegistry.Register<TMessage>(eventType, configure);
        return this;
    }
    
    /// <summary>
    /// Scans the assembly containing TMarker for [CloudEvent] decorated types.
    /// </summary>
    public CloudEventBusBuilder AddMessagesFromAssemblyContaining<TMarker>()
    {
        TypeRegistry.RegisterFromAssembly(typeof(TMarker).Assembly);
        return this;
    }
    
    /// <summary>
    /// Configures CloudEvents format options.
    /// </summary>
    public CloudEventBusBuilder ConfigureCloudEvents(Action<CloudEventsOptions> configure)
    {
        configure(CloudEventsOptions);
        return this;
    }
    
    /// <summary>
    /// Disables CloudEvents envelope wrapping.
    /// </summary>
    public CloudEventBusBuilder DisableCloudEvents()
    {
        CloudEventsOptions.Enabled = false;
        return this;
    }
    
    /// <summary>
    /// Registers a message handler.
    /// </summary>
    public CloudEventBusBuilder AddHandler<TMessage, THandler>()
        where TMessage : notnull
        where THandler : class, IMessageHandler<TMessage>
    {
        var eventType = TypeRegistry.ResolveEventType(typeof(TMessage));
        if (string.IsNullOrEmpty(eventType))
        {
            throw new InvalidOperationException(
                $"Cannot register handler for {typeof(TMessage).Name}: " +
                "message type must be registered first via AddMessage<T>() or have [CloudEvent] attribute.");
        }
        
        HandlerRegistry.Register<TMessage, THandler>(eventType);
        Services.AddScoped<THandler>();
        Services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());
        
        return this;
    }
    
    /// <summary>
    /// Registers a message with its handler in one call.
    /// </summary>
    public CloudEventBusBuilder AddMessageWithHandler<TMessage, THandler>(string eventType)
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        AddMessage<TMessage>(eventType);
        
        HandlerRegistry.Register<TMessage, THandler>(eventType);
        Services.AddScoped<THandler>();
        Services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());
        
        return this;
    }
}
