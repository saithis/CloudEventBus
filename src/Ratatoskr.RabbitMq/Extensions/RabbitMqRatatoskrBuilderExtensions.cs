using Microsoft.Extensions.DependencyInjection;
using Ratatoskr.Core;

namespace Ratatoskr.RabbitMq.Extensions;

public static class RabbitMqRatatoskrBuilderExtensions
{
    public static RatatoskrBuilder UseRabbitMq(this RatatoskrBuilder builder, Action<RabbitMqOptions> configure)
    {
        var options = new RabbitMqOptions();
        configure.Invoke(options);
        
        // General
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<RabbitMqConnectionManager>();
        builder.Services.AddSingleton<IRabbitMqEnvelopeMapper, CloudEventsAmqpMapper>();
        builder.Services.AddSingleton<RabbitMqTopologyManager>();
        builder.Services.AddSingleton<TopologyMigrator>();
        
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
