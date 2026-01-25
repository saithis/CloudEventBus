using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConsumerHealthCheck : IHealthCheck
{
    private readonly RabbitMqConsumer _consumer;
    
    public RabbitMqConsumerHealthCheck(RabbitMqConsumer consumer)
    {
        _consumer = consumer;
    }
    
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        // Check if consumer is running and channels are healthy
        if (_consumer.IsHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ consumer is running"));
        }
        
        return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ consumer is not healthy"));
    }
}
