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
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
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
        
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler1);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler1);
            services.AddScoped<SecondTestEventHandler>(_ => handler2);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler2);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
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
        var (dispatcher, _, channelRegistry) = CreateDispatcher();
        channelRegistry.Freeze();
        
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
    public async Task DispatchAsync_DeserializationFails_ReturnsPermanentError()
    {
        // Arrange
        var handler = new TestEventHandler();
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => handler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
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
        result.Should().Be(DispatchResult.PermanentError);
        handler.HandledMessages.Should().BeEmpty();
    }

    [Test]
    public async Task DispatchAsync_HandlerThrows_ReturnsRecoverableError()
    {
        // Arrange
        var handler = new ThrowingTestEventHandler();
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ThrowingTestEventHandler>(_ => handler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
        };
        
        // Act
        var result =  await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.RecoverableError);
    }

    [Test]
    public async Task DispatchAsync_MultipleHandlersOneThrows_ThrowsAggregateException()
    {
        // Arrange
        var goodHandler = new TestEventHandler();
        var badHandler = new ThrowingTestEventHandler();
        
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<TestEventHandler>(_ => goodHandler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => goodHandler);
            services.AddScoped<ThrowingTestEventHandler>(_ => badHandler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => badHandler);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
        var testEvent = new TestEvent { Id = "123", Data = "test data" };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(testEvent));
        var context = new MessageProperties
        {
            Id = "event-123",
            Type = "test.event",
            Source = "/test",
        };
        
        // Act
        var result =  await dispatcher.DispatchAsync(body, context, CancellationToken.None);
        
        // Assert
        result.Should().Be(DispatchResult.RecoverableError);
    }

    [Test]
    public async Task DispatchAsync_UsesNewScopeForEachMessage()
    {
        // Arrange
        var handler = new ScopedServiceTestHandler();
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ScopedServiceTestHandler>(_ => handler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
            services.AddScoped<ScopedService>();
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
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
        
        // Assert - Each dispatch creates a new scope, so scoped service is new each time
        handler.ServiceIds.Should().HaveCount(2);
        handler.ServiceIds[0].Should().NotBe(handler.ServiceIds[1]);
    }

    [Test]
    public async Task DispatchAsync_PassesCorrectContextToHandler()
    {
        // Arrange
        var handler = new ContextCapturingHandler();
        var (dispatcher, _, channelRegistry) = CreateDispatcher(services => 
        {
            services.AddScoped<ContextCapturingHandler>(_ => handler);
            services.AddScoped<IMessageHandler<TestEvent>>(_ => handler);
        });
        
        var channel = new ChannelRegistration("test", ChannelType.EventConsume);
        channel.Messages.Add(new MessageRegistration(typeof(TestEvent), "test.event"));
        channelRegistry.Register(channel);
        channelRegistry.Freeze();
        
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

    private static (MessageDispatcher dispatcher, ServiceCollection services, ChannelRegistry channelRegistry) 
        CreateDispatcher(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        
        // Use ChannelRegistry instead of MessageTypeRegistry (which is internal/unused by Dispatcher ctor now)
        var channelRegistry = new ChannelRegistry();
        
        var deserializer = new JsonMessageSerializer();
        
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetService<IServiceScopeFactory>() 
            ?? services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        
        var dispatcher = new MessageDispatcher(
            channelRegistry,
            deserializer,
            scopeFactory,
            NullLogger<MessageDispatcher>.Instance);
        
        return (dispatcher, services, channelRegistry);
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
    
    public Task HandleAsync(TestEvent message, MessageProperties context, CancellationToken cancellationToken)
    {
        ServiceIds.Add(Guid.NewGuid()); // Simulate capturing service ID
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
