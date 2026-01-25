using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;
using TUnit.Core;

namespace CloudEventBus.Tests.Core;

public class MessageDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithRegisteredHandler_CallsHandler()
    {
        // Arrange
        var handler = new TestEventHandler();
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler);
        });
        
        handlerRegistry.Register<TestEvent, TestEventHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.Success);
        handler.HandledMessages.Should().HaveCount(1);
        handler.HandledMessages[0].Id.Should().Be("123");
        handler.HandledMessages[0].Data.Should().Be("test data");
    }

    [Test]
    public async Task DispatchAsync_WithMultipleHandlers_CallsAllHandlers()
    {
        // Arrange
        var handler1 = new TestEventHandler();
        var handler2 = new SecondTestEventHandler();
        
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler1);
            services.AddScoped<SecondTestEventHandler>(_ => handler2);
        });
        
        handlerRegistry.Register<TestEvent, TestEventHandler>("test.event");
        handlerRegistry.Register<TestEvent, SecondTestEventHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.Success);
        handler1.HandledMessages.Should().HaveCount(1);
        handler2.HandledMessages.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_NoHandlerRegistered_ReturnsNoHandlers()
    {
        // Arrange
        var (dispatcher, _, handlerRegistry) = CreateDispatcher();
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "unknown.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.NoHandlers);
    }

    [Test]
    public async Task DispatchAsync_DeserializationFails_ReturnsDeserializationFailed()
    {
        // Arrange
        var handler = new TestEventHandler();
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler);
        });
        
        handlerRegistry.Register<TestEvent, TestEventHandler>("test.event");
        handlerRegistry.Freeze();
        
        var invalidBody = Encoding.UTF8.GetBytes("not valid json");
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = invalidBody
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(invalidBody, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.DeserializationFailed);
        handler.HandledMessages.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchAsync_HandlerThrows_ThrowsAggregateException()
    {
        // Arrange
        var handler = new ThrowingTestEventHandler();
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ThrowingTestEventHandler>(_ => handler);
        });
        
        handlerRegistry.Register<TestEvent, ThrowingTestEventHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        Func<Task> act = async () => await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        await act.Should().ThrowAsync<AggregateException>()
            .WithMessage("*One or more handlers failed*");
    }

    [Test]
    public async Task DispatchAsync_MultipleHandlersOneThrows_ThrowsAggregateException()
    {
        // Arrange
        var goodHandler = new TestEventHandler();
        var badHandler = new ThrowingTestEventHandler();
        
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => goodHandler);
            services.AddScoped<ThrowingTestEventHandler>(_ => badHandler);
        });
        
        handlerRegistry.Register<TestEvent, TestEventHandler>("test.event");
        handlerRegistry.Register<TestEvent, ThrowingTestEventHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act
        Func<Task> act = async () => await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        await act.Should().ThrowAsync<AggregateException>();
        
        // First handler should still have been called
        goodHandler.HandledMessages.Should().HaveCount(1);
    }

    [Test]
    public async Task DispatchAsync_UsesNewScopeForEachMessage()
    {
        // Arrange
        var handler = new ScopedServiceTestHandler();
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ScopedServiceTestHandler>(_ => handler);
            services.AddScoped<ScopedService>();
        });
        
        handlerRegistry.Register<TestEvent, ScopedServiceTestHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
            RawBody = body
        };
        
        // Act - Dispatch twice
        await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert - Each dispatch creates a new scope, so scoped service is new each time
        handler.ServiceIds.Should().HaveCount(2);
        handler.ServiceIds[0].Should().NotBe(handler.ServiceIds[1]);
    }

    [Test]
    public async Task DispatchAsync_PassesCorrectContextToHandler()
    {
        // Arrange
        var handler = new ContextCapturingHandler();
        var (dispatcher, _, handlerRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ContextCapturingHandler>(_ => handler);
        });
        
        handlerRegistry.Register<TestEvent, ContextCapturingHandler>("test.event");
        handlerRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageEnvelope
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test-source",
            Subject = "test-subject",
            Time = DateTimeOffset.UtcNow,
            Headers = new Dictionary<string, string> { ["custom"] = "header" },
            RawBody = body
        };
        
        // Act
        await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        handler.CapturedContext.Should().NotBeNull();
        handler.CapturedContext!.Id.Should().Be("event-123");
        handler.CapturedContext.Type.Should().Be("test.event");
        handler.CapturedContext.Source.Should().Be("/test-source");
        handler.CapturedContext.Subject.Should().Be("test-subject");
        handler.CapturedContext.Headers.Should().ContainKey("custom");
        handler.CapturedContext.Headers["custom"].Should().Be("header");
    }

    private static (MessageDispatcher dispatcher, ServiceCollection services, MessageHandlerRegistry handlerRegistry) 
        CreateDispatcher(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        
        var handlerRegistry = new MessageHandlerRegistry();
        var typeRegistry = new MessageTypeRegistry();
        typeRegistry.Register<TestEvent>("test.event");
        typeRegistry.Freeze();
        
        var deserializer = new JsonMessageSerializer();
        
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetService<IServiceScopeFactory>() 
            ?? services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        
        var dispatcher = new MessageDispatcher(
            handlerRegistry,
            typeRegistry,
            deserializer,
            scopeFactory,
            NullLogger<MessageDispatcher>.Instance);
        
        return (dispatcher, services, handlerRegistry);
    }
}

// Scoped service for testing DI scopes
public class ScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

// Handler that uses scoped service
public class ScopedServiceTestHandler : IMessageHandler<TestEvent>
{
    public List<Guid> ServiceIds { get; } = new();
    
    public Task HandleAsync(TestEvent message, MessageEnvelope context, CancellationToken cancellationToken)
    {
        ServiceIds.Add(Guid.NewGuid()); // Simulate capturing service ID
        return Task.CompletedTask;
    }
}

// Handler that captures context
public class ContextCapturingHandler : IMessageHandler<TestEvent>
{
    public MessageEnvelope? CapturedContext { get; private set; }
    
    public Task HandleAsync(TestEvent message, MessageEnvelope context, CancellationToken cancellationToken)
    {
        CapturedContext = context;
        return Task.CompletedTask;
    }
}
