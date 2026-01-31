using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using CloudEventBus.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.Serializers.Json;

namespace CloudEventBus.Tests.Core;

public class MessageDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithRegisteredHandler_CallsHandler()
    {
        // Arrange
        var handler = new TestEventHandler();
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
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
        
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler1);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler2);
        });
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
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
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event");
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event", 
            Source = "/test",
        };
        
        // Act
        // NOTE: If type is registered but no handler in DI -> NoHandlers
        var result = await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.NoHandlers);
    }
    
    [Test]
    public async Task DispatchAsync_UnknownType_ReturnsNoHandlers()
    {
        // Arrange
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event");
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "unknown.event", 
            Source = "/test",
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
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var invalidBody = Encoding.UTF8.GetBytes("not valid json");
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(invalidBody, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.PermanentError); // Changed from DeserializationFailed which didn't exist in new code logic (Wait, I used PermanentError in new code)
        handler.HandledMessages.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchAsync_HandlerThrows_ReturnsRecoverableError()
    {
        // Logic changed: Exceptions inside handler loop are caught and contribute to "errors++"
        // And result becomes RecoverableError.
        // It does NOT throw AggregateException anymore.
        
        // Arrange
        var handler = new ThrowingTestEventHandler();
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
        };
        
        // Act
        var result = await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.RecoverableError);
    }


    [Test]
    public async Task DispatchAsync_UsesNewScopeForEachMessage()
    {
        // Arrange
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            // Scoped services
            services.AddScoped<ScopedService>();
            services.AddScoped<IMessageHandler<TestEvent>, ScopedServiceTestHandler>();
        });
        
        // Use a static list to capture results since handler is created in scope
        ScopedServiceTestHandler.ServiceIds.Clear();

        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
        };
        
        // Act - Dispatch twice
        await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        ScopedServiceTestHandler.ServiceIds.Should().HaveCount(2);
        ScopedServiceTestHandler.ServiceIds[0].Should().NotBe(ScopedServiceTestHandler.ServiceIds[1]);
    }

    [Test]
    public async Task DispatchAsync_PassesCorrectContextToHandler()
    {
        // Arrange
        var handler = new ContextCapturingHandler();
        var (dispatcher, _) = CreateDispatcher<TestEvent>("test.event", services => 
        {
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test-source",
            Subject = "test-subject",
            Time = DateTimeOffset.UtcNow,
            Headers = new Dictionary<string, string> { ["custom"] = "header" },
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

    private static (MessageDispatcher dispatcher, ServiceCollection services) 
        CreateDispatcher<TMessage>(string eventType, Action<ServiceCollection>? configure = null)
        where TMessage : class
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        
        var typeRegistry = new MessageTypeRegistry();
        typeRegistry.Register<TMessage>(eventType);
        typeRegistry.Freeze();
        
        var deserializer = new JsonMessageSerializer();
        
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetService<IServiceScopeFactory>() 
            ?? services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        
        var dispatcher = new MessageDispatcher(
            typeRegistry,
            deserializer,
            scopeFactory,
            NullLogger<MessageDispatcher>.Instance);
        
        return (dispatcher, services);
    }
}

// Scoped service for testing DI scopes
public class ScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

// Handler that uses scoped service
public class ScopedServiceTestHandler(ScopedService service) : IMessageHandler<TestEvent>
{
    public static List<Guid> ServiceIds { get; } = new();
    
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        ServiceIds.Add(service.Id); // Simulate capturing service ID
        return Task.CompletedTask;
    }
}

// Handler that captures context
public class ContextCapturingHandler : IMessageHandler<TestEvent>
{
    public MessageProperties? CapturedContext { get; private set; }
    
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        CapturedContext = context;
        return Task.CompletedTask;
    }
}
