using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox;
using Saithis.CloudEventBus.EfCoreOutbox.Internal;
using Saithis.CloudEventBus.EfCoreOutbox.Testing;
using Saithis.CloudEventBus.RabbitMq;
using Saithis.CloudEventBus.Testing;

namespace CloudEventBus.Tests.Fixtures;

/// <summary>
/// Helper methods for configuring services in tests
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds CloudEventBus with InMemoryMessageSender for testing
    /// </summary>
    public static IServiceCollection AddTestCloudEventBus(
        this IServiceCollection services,
        Action<CloudEventBusBuilder>? configure = null)
    {
        services.AddCloudEventBus(configure);
        services.AddInMemoryMessageSender();
        return services;
    }

    /// <summary>
    /// Adds outbox pattern with synchronous processor for testing.
    /// Note: Does NOT register the background OutboxProcessor - use SynchronousOutboxProcessor instead.
    /// </summary>
    public static IServiceCollection AddTestOutbox<TDbContext>(
        this IServiceCollection services,
        Action<OutboxBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext, IOutboxDbContext
    {
        var builder = new OutboxBuilder<TDbContext>(services);
        configure?.Invoke(builder);
        
        // Register options
        services.AddSingleton(Options.Create(builder.Options));
        
        // Add synchronous processor for explicit test control
        services.AddSynchronousOutboxProcessor<TDbContext>();
        
        return services;
    }

    /// <summary>
    /// Adds RabbitMQ message sender configured with test container
    /// </summary>
    public static IServiceCollection AddTestRabbitMq(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddRabbitMqMessageSender(options =>
        {
            options.ConnectionString = connectionString;
            options.DefaultExchange = "test.exchange";
            options.UsePublisherConfirms = true;
        });
        return services;
    }

    /// <summary>
    /// Adds PostgreSQL DbContext configured with test container.
    /// Includes outbox interceptor for converting staged messages to entities.
    /// </summary>
    public static IServiceCollection AddTestDbContext(
        this IServiceCollection services,
        string connectionString,
        bool withOutboxInterceptor = true)
    {
        // First register the DbContext without interceptor
        services.AddDbContext<TestDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            
            // If outbox is needed, configure the interceptor
            if (withOutboxInterceptor)
            {
                var messageSerializer = sp.GetRequiredService<Saithis.CloudEventBus.Core.IMessageSerializer>();
                var timeProvider = sp.GetRequiredService<TimeProvider>();
                var interceptor = new TestOutboxInterceptor<TestDbContext>(messageSerializer, timeProvider);
                options.AddInterceptors(interceptor);
            }
        });
        
        return services;
    }
    
    /// <summary>
    /// Test-specific outbox interceptor that doesn't trigger background processing.
    /// Converts staged messages to entities but doesn't call TriggerAsync.
    /// </summary>
    private class TestOutboxInterceptor<TDbContext> : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
        where TDbContext : DbContext, IOutboxDbContext
    {
        private readonly Saithis.CloudEventBus.Core.IMessageSerializer _messageSerializer;
        private readonly TimeProvider _timeProvider;

        public TestOutboxInterceptor(
            Saithis.CloudEventBus.Core.IMessageSerializer messageSerializer,
            TimeProvider timeProvider)
        {
            _messageSerializer = messageSerializer;
            _timeProvider = timeProvider;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context == null)
                return ValueTask.FromResult(result);

            if (context is not IOutboxDbContext outboxDbContext)
                return ValueTask.FromResult(result);

            // Access the internal Queue via reflection
            var queueField = typeof(OutboxStagingCollection)
                .GetField("Queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var queue = queueField.GetValue(outboxDbContext.OutboxMessages);
            
            var queueType = queue!.GetType();
            var tryPeekMethod = queueType.GetMethod("TryPeek");
            var tryDequeueMethod = queueType.GetMethod("TryDequeue");
            
            // Process all items in queue
            while (outboxDbContext.OutboxMessages.Count > 0)
            {
                var peekParams = new object?[] { null };
                var hasPeeked = (bool)tryPeekMethod!.Invoke(queue, peekParams)!;
                
                if (!hasPeeked)
                    break;
                
                var item = peekParams[0]!;
                var itemType = item.GetType();
                var messageProperty = itemType.GetProperty("Message", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var propertiesProperty = itemType.GetProperty("Properties", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                
                var message = messageProperty.GetValue(item)!;
                var properties = (MessageProperties)propertiesProperty.GetValue(item)!;
                
                var serializedMessage = _messageSerializer.Serialize(message, properties);
                var outboxMessage = OutboxMessageEntity.Create(serializedMessage, properties, _timeProvider);
                context.Set<OutboxMessageEntity>().Add(outboxMessage);
                
                // Dequeue after successful serialization
                tryDequeueMethod!.Invoke(queue, new object?[] { null });
            }

            return ValueTask.FromResult(result);
        }
    }
}
