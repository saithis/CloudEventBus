using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ratatoskr.CloudEvents;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;
using System.Text;
using TUnit.Core;

namespace Ratatoskr.Tests.Integration;

public class RetryTests(RabbitMqContainerFixture rabbitMq, PostgresContainerFixture postgres) : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string ExchangeName => $"retry-test-{TestId}";
    private string QueueName => $"retry-queue-{TestId}";
    private string RetryQueue => $"{QueueName}.retry";
    private string DlqName => $"{QueueName}.dlq";

    [Test]
    public async Task Consume_HandlerThrows_MovesToRetryQueue()
    {
        // Arrange
        var handler = new ThrowingTestEventHandler();
        
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .AutoAck(false)
                        .RetryOptions(maxRetries: 3, delay: TimeSpan.FromSeconds(2), useManaged: true)
                        .QueueOptions(durable: false, autoDelete: true))
                    .Consumes<TestEvent>());
                bus.AddHandler<TestEvent, ThrowingTestEventHandler>(handler);
            });
        });

        try
        {
            // Act
            await PublishToRabbitMqAsync(exchange: "", routingKey: QueueName, new TestEvent { Id = "retry-1", Data = "fail" });

            // Assert - Message processed (invoked)
            // Cannot check handler.HandledMessages as ThrowingTestEventHandler doesn't track them.
            // We rely on the message moving to the retry queue.
            
            // Wait for message to move to retry queue (TTL not expired yet)
            await Task.Delay(500);
            
            var retryCount = await GetMessageCountAsync(RetryQueue);
            retryCount.Should().BeGreaterThan(0);
        }
        finally
        {
            // Consumer stops with factory dispose
        }
    }

    [Test]
    public async Task Consume_MaxRetriesExceeded_MovesToDlq()
    {
        // Arrange
        var handler = new ThrowingTestEventHandler();
        
        await StartTestAsync(services =>
        {
            // Fast retries for test
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .AutoAck(false)
                        .RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(100), useManaged: true)
                        .QueueOptions(durable: false, autoDelete: true))
                    .Consumes<TestEvent>());
                bus.AddHandler<TestEvent, ThrowingTestEventHandler>(handler);
            });
        });

        try
        {
            // Act
            await PublishToRabbitMqAsync(exchange: "", routingKey: QueueName, new TestEvent { Id = "dlq-1", Data = "fail" });

            // Wait for Retries + DLQ move
            // 2 Retries * 100ms + some buffer
            await Task.Delay(3000);
            
            // Assert
            var dlqCount = await GetMessageCountAsync(DlqName);
            dlqCount.Should().Be(1);
            
            var mainQueueCount = await GetMessageCountAsync(QueueName);
            mainQueueCount.Should().Be(0);
        }
        finally
        {
            // Consumer stops with factory dispose
        }
    }

    [Test]
    public async Task Consume_NoHandler_MovesToDlq()
    {
        // Arrange - No handlers registered
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => o.ConnectionString = RabbitMqConnectionString);
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .AutoAck(false)
                        .RetryOptions(maxRetries: 2, delay: TimeSpan.FromMilliseconds(100), useManaged: true)
                        .QueueOptions(durable: false, autoDelete: true)));
                    // No Consumes<> or AddHandler<> for the event type we send
            });
        });

        try
        {
            // Act - Send unknown event type
            
            // We publish directly to queue
            await PublishToRabbitMqAsync(exchange: "", routingKey: QueueName, new TestEvent { Id = "nohandler", Data = "fail" }, type: "unknown.event");

            // Wait for processing
            await Task.Delay(2000);

            // Assert - Should go to DLQ immediately (Permanent Error: No Handler)
            var dlqCount = await GetMessageCountAsync(DlqName);
            dlqCount.Should().Be(1);
        }
        finally
        {
            // Consumer stops with factory dispose
        }
    }
    
    // Helper needed for throwing handler because it doesn't expose HandledMessages
    // Wait, throwing handler doesn't add to list?
    // In TestMessages.cs:
    /*
    public class ThrowingTestEventHandler : IMessageHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Handler failed intentionally");
        }
    }
    */
    // So "await WaitForConditionAsync(() => handler.HandledMessages.Count > 0" will FAIL because it has no HandledMessages property and doesn't add anything.
    // I need to fix this test logic. 
    // I should check if the message moved to retry queue instead of checking handler count.
    // But handler invocation is instantaneous before throw.
    // If I cant check invocations, I just check the queue count.
}
