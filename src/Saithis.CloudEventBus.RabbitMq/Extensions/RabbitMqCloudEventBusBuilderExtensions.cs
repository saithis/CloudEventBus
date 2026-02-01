using Microsoft.Extensions.DependencyInjection;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqCloudEventBusBuilderExtensions
{
    public static CloudEventBusBuilder UseRabbitMq(this CloudEventBusBuilder builder, Action<RabbitMqOptions> configure)
    {
        var options = new RabbitMqOptions();
        configure.Invoke(options);
        
        // General
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<RabbitMqConnectionManager>();
        builder.Services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        builder.Services.AddSingleton<RabbitMqTopologyManager>();
        
        // Sending
        builder.Services.AddSingleton<ITransportMessageMetadataEnricher, RabbitMqMessageMetadataEnricher>();
        builder.Services.AddSingleton<IMessageSender, RabbitMqMessageSender>();
        
        // Consuming
        builder.Services.AddSingleton<MessageDispatcher>();
        builder.Services.AddSingleton<RabbitMqRetryHandler>();
        builder.Services.AddHostedService<RabbitMqConsumer>();
        
        return builder;
    }
}
