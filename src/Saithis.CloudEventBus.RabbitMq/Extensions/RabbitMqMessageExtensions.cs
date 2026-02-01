using Saithis.CloudEventBus.Config;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.RabbitMq.Config;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqMessageExtensions
{
    private const string MessageOptionsKey = "RabbitMqMessageOptions";
    
    extension(MessageRegistration registration)
    {
        public RabbitMqMessageOptions EnsureRabbitMqOptions()
        {
            if (!registration.Metadata.TryGetValue(MessageOptionsKey, out var options))
            {
                options = new RabbitMqMessageOptions();
                registration.Metadata[MessageOptionsKey] = options;
            }

            if(!(options is RabbitMqMessageOptions rabbitMqOptions))
                throw new InvalidOperationException($"{typeof(MessageRegistration)}.{nameof(registration.Metadata)}[{MessageOptionsKey}] must be of type {nameof(RabbitMqMessageOptions)}.");
            return rabbitMqOptions;
        }

        public RabbitMqMessageOptions? GetRabbitMqOptions() => registration.Metadata.GetValueOrDefault(MessageOptionsKey) as RabbitMqMessageOptions;
    }
    
    public static MessageBuilder WithRoutingKey(this MessageBuilder builder, string routingKey)
    {
        builder.MessageRegistration.EnsureRabbitMqOptions().WithRoutingKey(routingKey);
        return builder;
    }
}