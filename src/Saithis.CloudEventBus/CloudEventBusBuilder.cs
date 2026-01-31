using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Configuration;

namespace Saithis.CloudEventBus;

public class CloudEventBusBuilder
{
    public IServiceCollection Services { get; }
    internal MessageTypeRegistry TypeRegistry { get; } = new();
    internal CloudEventsOptions CloudEventsOptions { get; } = new();
    internal MessageHandlerRegistry HandlerRegistry { get; } = new();
    internal ProductionRegistry ProductionRegistry { get; } = new();
    internal ConsumptionRegistry ConsumptionRegistry { get; } = new();
    
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
    /// Registers a message handler.
    /// </summary>
    public CloudEventBusBuilder AddHandler<TMessage, THandler>()
        where TMessage : notnull
        where THandler : class, IMessageHandler<TMessage>
    {
        var eventType = TypeRegistry.ResolveEventType(typeof(TMessage));
        // We relax the check to allow delayed registration or registration via new API
        // HandlerRegistry.Register<TMessage, THandler>(eventType ?? typeof(TMessage).Name);
        
        // Actually, let's keep it simple: if eventType is null, try to resolve it later?
        // For now, let's rely on TypeRegistry still being populated?
        // Or we can just register it and let the dispatcher handle it.
        // The HandlerRegistry uses eventType as key. If we don't have it, we can't register it properly there yet.
        // BUT, the new plan says: "Dispatcher resolves string type -> Type messageType via ConsumptionRegistry".
        // Then resolved Type -> Handlers via DI.
        // So the HandlerRegistry (which maps string type -> Handler Type) might become obsolete or secondary?
        // Plan says: "Dispatcher invokes HandleAsync on all found handlers."
        // "Dispatcher resolves IEnumerable<IMessageHandler<UserRegistered>> from Service Provider".
        // SO, we just need to register THandler as IMessageHandler<TMessage>.
        // We DON'T strictly need HandlerRegistry anymore if we use the generic interface injection!
        
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
        AddHandler<TMessage, THandler>();
        return this;
    }
    
    // ========================================================================
    // New Intent-Based API
    // ========================================================================

    public ProductionBuilder<T> ProducesEvent<T>(string channelName)
    {
        var reg = new ProductionRegistration(typeof(T), MessageIntent.EventProduction, channelName);
        ProductionRegistry.Add(reg);
        return new ProductionBuilder<T>(reg);
    }

    public ProductionBuilder<T> SendsCommand<T>(string channelName)
    {
        var reg = new ProductionRegistration(typeof(T), MessageIntent.CommandSending, channelName);
        ProductionRegistry.Add(reg);
        return new ProductionBuilder<T>(reg);
    }

    public ConsumptionBuilder<T> ConsumesCommand<T>(string queueName)
    {
        var reg = new ConsumptionRegistration(typeof(T), MessageIntent.CommandConsumption, string.Empty, queueName);
        ConsumptionRegistry.Add(reg);
        return new ConsumptionBuilder<T>(reg);
    }

    public ConsumptionBuilder<T> ConsumesEvent<T>(string queueName)
    {
        var reg = new ConsumptionRegistration(typeof(T), MessageIntent.EventConsumption, string.Empty, queueName);
        ConsumptionRegistry.Add(reg);
        return new ConsumptionBuilder<T>(reg);
    }
}
