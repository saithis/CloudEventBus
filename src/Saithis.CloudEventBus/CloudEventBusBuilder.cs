using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus;

public class CloudEventBusBuilder
{
    internal IServiceCollection Services { get; }
    internal MessageTypeRegistry TypeRegistry { get; } = new();
    
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
}
