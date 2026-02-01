using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Ratatoskr.Tests.Fixtures;
using Ratatoskr.EfCore.Internal;
using Ratatoskr.EfCore.Testing;
using Ratatoskr.Testing;
using TUnit.Core;

namespace Ratatoskr.Tests.Outbox;

[ClassDataSource<PostgresContainerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("PostgreSQL")] // Tests use shared database
public class OutboxIntegrationTests(PostgresContainerFixture postgres)
{
    [Test]
    public async Task SaveChanges_WithStagedMessages_CreatesOutboxEntities()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>();
        
        var provider = services.BuildServiceProvider();
        
        // Ensure database is created
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            // Stage messages
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 1" });
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test 2" });
            
            await dbContext.SaveChangesAsync();
        }
        
        // Assert
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var outboxEntities = await dbContext.Set<OutboxMessageEntity>().ToListAsync();
            
            outboxEntities.Should().HaveCount(2);
            outboxEntities.All(e => e.ProcessedAt == null).Should().BeTrue();
            outboxEntities.All(e => !e.IsPoisoned).Should().BeTrue();
        }
    }

    [Test]
    public async Task ProcessAllAsync_SendsAllPendingMessages()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>();
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Stage messages
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "message 1" });
            dbContext.OutboxMessages.Add(new TestEvent { Data = "message 2" });
            dbContext.OutboxMessages.Add(new TestEvent { Data = "message 3" });
            await dbContext.SaveChangesAsync();
        }
        
        // Act
        int processedCount;
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            processedCount = await processor.ProcessAllAsync();
        }
        
        // Assert
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        processedCount.Should().Be(3);
        sender.SentMessages.Should().HaveCount(3);
        
        // Verify all messages are marked as processed
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entities = await dbContext.Set<OutboxMessageEntity>().ToListAsync();
            entities.All(e => e.ProcessedAt != null).Should().BeTrue();
        }
    }

    [Test]
    public async Task ProcessAllAsync_WithFailingSender_RetriesWithBackoff()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var failingSender = new FailingMessageSender(failuresBeforeSuccess: 2);
        
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>();
        
        // Replace InMemoryMessageSender with FailingMessageSender
        services.AddSingleton<global::Ratatoskr.Core.IMessageSender>(failingSender);
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Stage message
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test" });
            await dbContext.SaveChangesAsync();
        }
        
        // Act - First attempt (will fail)
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        // Assert - Message should not be processed, should have retry scheduled
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entity = await dbContext.Set<OutboxMessageEntity>().FirstAsync();
            
            entity.ProcessedAt.Should().BeNull();
            entity.ErrorCount.Should().Be(1);
            entity.NextAttemptAt.Should().NotBeNull();
            entity.IsPoisoned.Should().BeFalse();
        }
        
        // Advance time past retry delay
        fakeTime.Advance(TimeSpan.FromSeconds(3));
        
        // Act - Second attempt (will also fail)
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        // Assert
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entity = await dbContext.Set<OutboxMessageEntity>().FirstAsync();
            
            entity.ErrorCount.Should().Be(2);
        }
        
        // Advance time again
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        
        // Act - Third attempt (will succeed)
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        // Assert - Should now be processed
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entity = await dbContext.Set<OutboxMessageEntity>().FirstAsync();
            
            entity.ProcessedAt.Should().NotBeNull();
            entity.ErrorCount.Should().Be(2); // Still 2, last attempt succeeded
            failingSender.CallCount.Should().Be(3);
        }
    }

    [Test]
    public async Task ProcessAllAsync_AfterMaxRetries_MarksPoisoned()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var alwaysFailingSender = new FailingMessageSender(); // Never succeeds
        
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>(outbox => outbox.WithMaxRetries(3));
        services.AddSingleton<global::Ratatoskr.Core.IMessageSender>(alwaysFailingSender);
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Stage message
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            dbContext.OutboxMessages.Add(new TestEvent { Data = "test" });
            await dbContext.SaveChangesAsync();
        }
        
        // Act - Try processing 3 times (max retries)
        for (int i = 0; i < 3; i++)
        {
            using var scope = provider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
            
            // Advance time for next retry
            fakeTime.Advance(TimeSpan.FromSeconds(10));
        }
        
        // Assert - Should be marked as poisoned
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entity = await dbContext.Set<OutboxMessageEntity>().FirstAsync();
            
            entity.IsPoisoned.Should().BeTrue();
            entity.ProcessedAt.Should().BeNull();
            entity.ErrorCount.Should().Be(3);
            entity.NextAttemptAt.Should().BeNull(); // No more retries
        }
        
        // Try processing again - should not attempt poisoned message
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            var count = await processor.ProcessAllAsync();
            count.Should().Be(0);
        }
    }

    [Test]
    public async Task SaveChanges_TransactionalWithEntity_BothCommitted()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>();
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Act - Save entity and outbox message in same transaction
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            var entity = new TestEntity 
            { 
                Name = "Test Entity",
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.TestEntities.Add(entity);
            
            dbContext.OutboxMessages.Add(new TestEvent { Data = "event for entity" });
            
            await dbContext.SaveChangesAsync();
        }
        
        // Assert - Both should be saved
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            
            var entities = await dbContext.TestEntities.ToListAsync();
            entities.Should().HaveCount(1);
            entities[0].Name.Should().Be("Test Entity");
            
            var outboxMessages = await dbContext.Set<OutboxMessageEntity>().ToListAsync();
            outboxMessages.Should().HaveCount(1);
        }
    }

    [Test]
    public async Task ProcessAllAsync_ProcessesInBatches()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddTestCloudEventBus(bus => bus.AddEventPublishChannel("test", c => c.Produces<TestEvent>()));
        services.AddTestDbContext(postgres.ConnectionString);
        services.AddTestOutbox<TestDbContext>(outbox => outbox.WithBatchSize(2)); // Small batch
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            await dbContext.Set<OutboxMessageEntity>().ExecuteDeleteAsync();
            await dbContext.TestEntities.ExecuteDeleteAsync();
        }
        
        // Stage 5 messages (more than batch size)
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            for (int i = 1; i <= 5; i++)
            {
                dbContext.OutboxMessages.Add(new TestEvent { Data = $"message {i}" });
            }
            await dbContext.SaveChangesAsync();
        }
        
        // Act
        using (var scope = provider.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SynchronousOutboxProcessor<TestDbContext>>();
            await processor.ProcessAllAsync();
        }
        
        // Assert - All should be processed despite small batch size
        var sender = provider.GetRequiredService<InMemoryMessageSender>();
        sender.SentMessages.Should().HaveCount(5);
        
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var entities = await dbContext.Set<OutboxMessageEntity>().ToListAsync();
            entities.All(e => e.ProcessedAt != null).Should().BeTrue();
        }
    }
}
