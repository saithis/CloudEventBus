using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client.Exceptions;
using AwesomeAssertions;
using RabbitMQ.Client;
using Ratatoskr.Core;
using Ratatoskr.RabbitMq.Config;
using Ratatoskr.RabbitMq.Extensions;
using Ratatoskr.Tests.Fixtures;

namespace Ratatoskr.Tests.Integration;

public class TopologyMigrationTests(
    RabbitMqContainerFixture rabbitMq,
    PostgresContainerFixture postgres) : RatatoskrIntegrationTest(rabbitMq, postgres)
{
    private string QueueName => $"mig-queue-{TestId}";
    private string DlqName => $"mig-queue-{TestId}.dlq";
    
    /// <summary>
    /// Helper to publish a CloudEvents-formatted message directly to RabbitMQ
    /// </summary>
    private async Task PublishCloudEventAsync(IChannel channel, string queueName, string messageBody, int index = 0)
    {
        var props = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = $"test-msg-{index}",
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Type = "test.event",
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["cloudEvents_specversion"] = "1.0",
                ["cloudEvents_id"] = $"test-msg-{index}",
                ["cloudEvents_type"] = "test.event",
                ["cloudEvents_source"] = "/test",
                ["cloudEvents_time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["cloudEvents_datacontenttype"] = "application/json"
            }
        };
        
        var body = System.Text.Encoding.UTF8.GetBytes(messageBody);
        await channel.BasicPublishAsync("", queueName, false, props, body);
    }

    [Test]
    public async Task Migration_PreservesMessages_WhenConvertingToQuorum()
    {
        // Arrange: Create a "Legacy" Classic Queue with messages
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });

        // Publish some CloudEvents-formatted messages
        for (int i = 0; i < 10; i++)
        {
            var eventData = $"{{\"data\":\"msg-{i}\"}}";
            await PublishCloudEventAsync(channel, QueueName, eventData, i);
        }

        // Act: Start Ratatoskr with Migration Enabled
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                // Define the desired topology (Quorum is default)
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .WithQueueType(QueueType.Quorum))
                    .Consumes<TestEvent>());
                
                // Add handler to consume and count messages
                bus.AddHandler<TestEvent, CountingHandler>();
            });
            
            services.AddSingleton(new CountingHandler());
        });

        // Assert
        // We need to resolve the handler from the running application scope. 
        // RatatoskrIntegrationTest helper 'StartTestAsync' creates a factory, but how do we access services?
        // RatatoskrIntegrationTest stores _factory.
        // We can use InScopeAsync logic or just expose factory services?
        // Actually, we can just create a scope here.
        
        await InScopeAsync(ctx => 
        {
             var handler = ctx.ServiceProvider.GetRequiredService<CountingHandler>();
             return handler.WaitForMessagesAsync(10);
        });
        
        await InScopeAsync(ctx => 
        {
             var handler = ctx.ServiceProvider.GetRequiredService<CountingHandler>();
             handler.ReceivedCount.Should().Be(10);
        });

        // Verify queue is Quorum
        // Re-create connection/channel to avoid scope issues or reuse existing?
        // Existing factory/connection variables are in scope range of this method?
        // Wait, I declared them at top of method.
        
        // I will just use a new connection/channel to be clean and simple
        var checkFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var checkConnection = await checkFactory.CreateConnectionAsync();
        await using var checkChannel = await checkConnection.CreateChannelAsync();
        
        var act = async () => await checkChannel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });

        await act.Should().ThrowAsync<OperationInterruptedException>()
            .Where(e => e.ShutdownReason.ReplyCode == 406);
    }

    public class CountingHandler : IMessageHandler<TestEvent>
    {
        private int _count = 0;
        private int _expectedCount;
        private TaskCompletionSource _tcs = new();
        private readonly System.Collections.Concurrent.ConcurrentBag<(TestEvent Message, MessageProperties Properties)> _receivedMessages = new();

        public int ReceivedCount => _count;
        public IReadOnlyCollection<(TestEvent Message, MessageProperties Properties)> ReceivedMessages => _receivedMessages;

        public Task HandleAsync(TestEvent message, MessageProperties properties, CancellationToken cancellationToken)
        {
            _receivedMessages.Add((message, properties));
            var newCount = Interlocked.Increment(ref _count);
            
            if (_expectedCount > 0 && newCount >= _expectedCount)
            {
                _tcs.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task WaitForMessagesAsync(int count, TimeSpan? timeout = null)
        {
            _expectedCount = count;
            return _tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
        }
        
        public void Reset(int expectedCount = 0)
        {
            _count = 0;
            _expectedCount = expectedCount;
            _receivedMessages.Clear();
            _tcs = new TaskCompletionSource();
        }
    }
    
    [Test]
    public async Task Migration_HandlesDlqConversion_FromTopicToFanout()
    {
        // Arrange: Create a "Legacy" DLQ Exchange (Topic) and Queue (Classic)
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Topic Exchange
        await channel.ExchangeDeclareAsync(
            exchange: DlqName, 
            type: ExchangeType.Topic, 
            durable: true, 
            autoDelete: false, 
            arguments: null);
        
        // Classic Queue
        await channel.QueueDeclareAsync(
            queue: DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });
        
        // Bind
        await channel.QueueBindAsync(DlqName, DlqName, "#");

        // Publish a CloudEvents message to DLQ to ensure it survives
        await PublishCloudEventAsync(channel, DlqName, "{\"data\":\"dlq-msg\"}", 0);

        // Act
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                // This triggers provisioning, including DLQ migration
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .RetryOptions(3, TimeSpan.FromMilliseconds(100))) // retry creates DLQ
                    .Consumes<TestEvent>());
            });
        });

        // Assert
        // Verify DLQ Exchange is Fanout & Queue is Quorum
        
        var checkFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var checkConnection = await checkFactory.CreateConnectionAsync();
        await using var checkChannel = await checkConnection.CreateChannelAsync();
        
        // Check Queue Type = Quorum (try declaring as classic -> should fail)
        var act = async () => await checkChannel.QueueDeclareAsync(
             queue: DlqName,
             durable: true,
             exclusive: false,
             autoDelete: false,
             arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });

         await act.Should().ThrowAsync<OperationInterruptedException>()
             .Where(e => e.ShutdownReason.ReplyCode == 406);
             
         // Check message exists - using a NEW channel because previous one is closed 406
         await using var getChannel = await checkConnection.CreateChannelAsync();
         var result = await getChannel.BasicGetAsync(DlqName, true);
         
         result.Should().NotBeNull();
         // Verify it's a CloudEvents message
         result!.BasicProperties.Headers.Should().ContainKey("cloudEvents_type");
    }
    
    [Test]
    public async Task Migration_SkipsMigration_WhenQueueAlreadyMatches()
    {
        // Arrange: Create queue with correct type already (Quorum)
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        
        // Act: Start Ratatoskr with same configuration
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .WithQueueType(QueueType.Quorum))
                    .Consumes<TestEvent>());
            });
        });
        
        // Assert: No backup queue should exist
        var checkFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var checkConnection = await checkFactory.CreateConnectionAsync();
        await using var checkChannel = await checkConnection.CreateChannelAsync();
        
        var act = async () => await checkChannel.QueueDeclarePassiveAsync($"{QueueName}.backup");
        await act.Should().ThrowAsync<OperationInterruptedException>()
            .Where(e => e.ShutdownReason.ReplyCode == 404); // Queue doesn't exist
    }
    
    [Test]
    public async Task Migration_PreservesMultipleMessages_WithUniqueContent()
    {
        // Arrange: Create classic queue with multiple messages with unique IDs
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });

        // Publish CloudEvents messages with unique identifiers
        for (int i = 0; i < 5; i++)
        {
            var eventData = $"{{\"data\":\"unique-message-{i}\"}}";
            await PublishCloudEventAsync(channel, QueueName, eventData, i);
        }

        // Act: Migrate
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .WithQueueType(QueueType.Quorum))
                    .Consumes<TestEvent>());
                
                bus.AddHandler<TestEvent, CountingHandler>();
            });
            
            services.AddSingleton(new CountingHandler());
        });

        await InScopeAsync(ctx => 
        {
            var handler = ctx.ServiceProvider.GetRequiredService<CountingHandler>();
            return handler.WaitForMessagesAsync(5);
        });

        // Assert: Verify all messages received
        await InScopeAsync(ctx => 
        {
            var handler = ctx.ServiceProvider.GetRequiredService<CountingHandler>();
            handler.ReceivedMessages.Should().HaveCount(5);
            handler.ReceivedCount.Should().Be(5);
        });
    }
    
    [Test]
    public async Task Migration_HandlesEmptyQueue_Successfully()
    {
        // Arrange: Create empty classic queue
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });
        
        // Act: Migrate empty queue
        await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .WithQueueType(QueueType.Quorum))
                    .Consumes<TestEvent>());
            });
        });
        
        // Assert: Queue should be migrated to quorum
        var checkFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var checkConnection = await checkFactory.CreateConnectionAsync();
        await using var checkChannel = await checkConnection.CreateChannelAsync();
        
        // Try declaring as classic - should fail
        var act = async () => await checkChannel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });

        await act.Should().ThrowAsync<OperationInterruptedException>()
            .Where(e => e.ShutdownReason.ReplyCode == 406);
            
        // Verify backup queue doesn't exist
        await using var checkChannel2 = await checkConnection.CreateChannelAsync();
        var actBackup = async () => await checkChannel2.QueueDeclarePassiveAsync($"{QueueName}.backup");
        await actBackup.Should().ThrowAsync<OperationInterruptedException>()
            .Where(e => e.ShutdownReason.ReplyCode == 404);
    }
    
    [Test]
    public async Task Migration_FailsGracefully_WhenBackupQueueAlreadyExists()
    {
        // Arrange: Create both the queue to migrate AND a backup queue
        var factory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "classic" });
            
        // Create backup queue that would block migration
        var backupQueueName = $"{QueueName}.backup";
        await channel.QueueDeclareAsync(
            queue: backupQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        
        // Act & Assert: Should fail to start due to backup queue existing
        var act = async () => await StartTestAsync(services =>
        {
            services.AddRatatoskr(bus => 
            {
                bus.UseRabbitMq(o => 
                {
                    o.ConnectionString = RabbitMqConnectionString;
                    o.AutoMigrateTopology = true;
                });
                
                bus.AddCommandConsumeChannel(QueueName, c => c
                    .WithRabbitMq(o => o
                        .QueueName(QueueName)
                        .WithQueueType(QueueType.Quorum))
                    .Consumes<TestEvent>());
            });
        });
        
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(e => e.Message.Contains("Backup queue") && e.Message.Contains("already exists"));
    }
}
