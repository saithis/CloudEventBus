# Plan 08: Add Message Consuming Support

## Priority: 8 (Major Feature)

## Depends On
- Plan 03 (RabbitMQ Sender - for understanding transport layer)
- Plan 04 (Message Type Registration - for type resolution)
- Plan 05 (CloudEvents Format - for deserialization)

## Problem

The library only supports publishing messages. There's no way to:
- Receive messages from a broker
- Deserialize to the correct type
- Handle messages with business logic
- Configure which queues/topics to listen to

## Solution

Implement a complete message consuming infrastructure:
1. `IMessageHandler<T>` interface for handlers
2. Message handler registry
3. Message deserializer that uses type registry
4. RabbitMQ consumer with queue listeners
5. Hosting integration for background consumers

---

## Part 1: Core Consuming Abstractions

### 1.1 Create Message Handler Interface

Create `src/Saithis.CloudEventBus/Core/IMessageHandler.cs`:

```csharp
namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Handles messages of a specific type.
/// </summary>
/// <typeparam name="TMessage">The message type to handle</typeparam>
public interface IMessageHandler<in TMessage> where TMessage : notnull
{
    /// <summary>
    /// Handles the message.
    /// </summary>
    /// <param name="message">The deserialized message</param>
    /// <param name="context">Context about the message delivery</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context provided to message handlers.
/// </summary>
public class MessageContext
{
    /// <summary>
    /// The CloudEvent ID.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// The CloudEvent type.
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// The CloudEvent source.
    /// </summary>
    public required string Source { get; init; }
    
    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset? Time { get; init; }
    
    /// <summary>
    /// CloudEvent subject (optional).
    /// </summary>
    public string? Subject { get; init; }
    
    /// <summary>
    /// All headers from the message.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    
    /// <summary>
    /// The raw message body bytes.
    /// </summary>
    public required byte[] RawBody { get; init; }
}
```

### 1.2 Create Message Deserializer

Create `src/Saithis.CloudEventBus/Core/IMessageDeserializer.cs`:

```csharp
namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Deserializes incoming messages to their target types.
/// </summary>
public interface IMessageDeserializer
{
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    object? Deserialize(byte[] body, Type targetType, MessageContext context);
    
    /// <summary>
    /// Deserializes a message body to the specified type.
    /// </summary>
    TMessage? Deserialize<TMessage>(byte[] body, MessageContext context);
}
```

Create `src/Saithis.CloudEventBus/Serializers/Json/JsonMessageDeserializer.cs`:

```csharp
using System.Text.Json;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.Serializers.Json;

public class JsonMessageDeserializer : IMessageDeserializer
{
    private readonly CloudEventsOptions _cloudEventsOptions;
    
    public JsonMessageDeserializer(CloudEventsOptions cloudEventsOptions)
    {
        _cloudEventsOptions = cloudEventsOptions;
    }
    
    public object? Deserialize(byte[] body, Type targetType, MessageContext context)
    {
        if (_cloudEventsOptions.Enabled && 
            _cloudEventsOptions.ContentMode == CloudEventsContentMode.Structured)
        {
            // Parse CloudEvents envelope and extract data
            var envelope = JsonSerializer.Deserialize<CloudEventEnvelope>(body);
            if (envelope?.Data != null)
            {
                // Data is already deserialized as JsonElement, need to convert
                var dataJson = JsonSerializer.Serialize(envelope.Data);
                return JsonSerializer.Deserialize(dataJson, targetType);
            }
            return null;
        }
        
        // Binary mode or CloudEvents disabled - body is the raw data
        return JsonSerializer.Deserialize(body, targetType);
    }
    
    public TMessage? Deserialize<TMessage>(byte[] body, MessageContext context)
    {
        return (TMessage?)Deserialize(body, typeof(TMessage), context);
    }
}
```

### 1.3 Create Handler Registry

Create `src/Saithis.CloudEventBus/Core/MessageHandlerRegistry.cs`:

```csharp
using System.Collections.Frozen;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Registry of message handlers mapped by event type.
/// </summary>
public class MessageHandlerRegistry
{
    private readonly Dictionary<string, HandlerRegistration> _handlers = new();
    private FrozenDictionary<string, HandlerRegistration>? _frozen;
    private bool _isFrozen;
    
    /// <summary>
    /// Registers a handler for an event type.
    /// </summary>
    public void Register<TMessage, THandler>(string eventType)
        where TMessage : notnull
        where THandler : IMessageHandler<TMessage>
    {
        EnsureNotFrozen();
        
        _handlers[eventType] = new HandlerRegistration(
            typeof(TMessage),
            typeof(THandler),
            typeof(IMessageHandler<TMessage>));
    }
    
    /// <summary>
    /// Freezes the registry for optimal performance.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen) return;
        _frozen = _handlers.ToFrozenDictionary();
        _isFrozen = true;
    }
    
    /// <summary>
    /// Gets the handler registration for an event type.
    /// </summary>
    public HandlerRegistration? GetHandler(string eventType)
    {
        if (_isFrozen)
            return _frozen!.GetValueOrDefault(eventType);
        return _handlers.GetValueOrDefault(eventType);
    }
    
    /// <summary>
    /// Gets all registered event types.
    /// </summary>
    public IEnumerable<string> GetRegisteredEventTypes()
    {
        return _isFrozen ? _frozen!.Keys : _handlers.Keys;
    }
    
    private void EnsureNotFrozen()
    {
        if (_isFrozen)
            throw new InvalidOperationException("Registry is frozen.");
    }
}

public record HandlerRegistration(
    Type MessageType,
    Type HandlerType,
    Type HandlerInterfaceType);
```

---

## Part 2: Message Dispatcher

### 2.1 Create Message Dispatcher

Create `src/Saithis.CloudEventBus/Core/MessageDispatcher.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Saithis.CloudEventBus.Core;

/// <summary>
/// Dispatches incoming messages to their registered handlers.
/// </summary>
public class MessageDispatcher
{
    private readonly MessageHandlerRegistry _handlerRegistry;
    private readonly MessageTypeRegistry _typeRegistry;
    private readonly IMessageDeserializer _deserializer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageDispatcher> _logger;
    
    public MessageDispatcher(
        MessageHandlerRegistry handlerRegistry,
        MessageTypeRegistry typeRegistry,
        IMessageDeserializer deserializer,
        IServiceScopeFactory scopeFactory,
        ILogger<MessageDispatcher> logger)
    {
        _handlerRegistry = handlerRegistry;
        _typeRegistry = typeRegistry;
        _deserializer = deserializer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// Dispatches a message to its handler.
    /// </summary>
    public async Task<bool> DispatchAsync(byte[] body, MessageContext context, CancellationToken cancellationToken)
    {
        var registration = _handlerRegistry.GetHandler(context.Type);
        if (registration == null)
        {
            _logger.LogWarning("No handler registered for event type '{EventType}'", context.Type);
            return false;
        }
        
        try
        {
            var message = _deserializer.Deserialize(body, registration.MessageType, context);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize message of type '{EventType}'", context.Type);
                return false;
            }
            
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerInterfaceType);
            
            var handleMethod = registration.HandlerInterfaceType.GetMethod("HandleAsync")!;
            var task = (Task)handleMethod.Invoke(handler, [message, context, cancellationToken])!;
            await task;
            
            _logger.LogDebug("Successfully handled message '{Id}' of type '{Type}'", 
                context.Id, context.Type);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message '{Id}' of type '{Type}'", 
                context.Id, context.Type);
            throw;
        }
    }
}
```

---

## Part 3: RabbitMQ Consumer

### 3.1 Create Consumer Options

Create `src/Saithis.CloudEventBus.RabbitMq/RabbitMqConsumerOptions.cs`:

```csharp
namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConsumerOptions
{
    /// <summary>
    /// Queues to consume from, with their configurations.
    /// </summary>
    public List<QueueConsumerConfig> Queues { get; set; } = new();
    
    /// <summary>
    /// Number of messages to prefetch per consumer.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;
    
    /// <summary>
    /// Whether to auto-acknowledge messages (not recommended for production).
    /// </summary>
    public bool AutoAck { get; set; } = false;
}

public class QueueConsumerConfig
{
    /// <summary>
    /// Name of the queue to consume from.
    /// </summary>
    public required string QueueName { get; init; }
    
    /// <summary>
    /// Event types expected in this queue (for routing).
    /// If empty, accepts any registered event type.
    /// </summary>
    public List<string> EventTypes { get; init; } = new();
}
```

### 3.2 Create RabbitMQ Consumer

Create `src/Saithis.CloudEventBus.RabbitMq/RabbitMqConsumer.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Saithis.CloudEventBus.CloudEvents;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqConsumer : BackgroundService
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqConsumerOptions _options;
    private readonly MessageDispatcher _dispatcher;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly List<IChannel> _channels = new();
    
    public RabbitMqConsumer(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConsumerOptions options,
        MessageDispatcher dispatcher,
        ILogger<RabbitMqConsumer> logger)
    {
        _connectionManager = connectionManager;
        _options = options;
        _dispatcher = dispatcher;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RabbitMQ consumer");
        
        foreach (var queueConfig in _options.Queues)
        {
            var channel = await _connectionManager.CreateChannelAsync(stoppingToken);
            await channel.BasicQosAsync(0, _options.PrefetchCount, false, stoppingToken);
            
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                await HandleMessageAsync(channel, ea, stoppingToken);
            };
            
            await channel.BasicConsumeAsync(
                queue: queueConfig.QueueName,
                autoAck: _options.AutoAck,
                consumer: consumer,
                cancellationToken: stoppingToken);
            
            _channels.Add(channel);
            _logger.LogInformation("Started consuming from queue '{Queue}'", queueConfig.QueueName);
        }
        
        // Keep running until cancelled
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private async Task HandleMessageAsync(
        IChannel channel, 
        BasicDeliverEventArgs ea,
        CancellationToken cancellationToken)
    {
        var messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
        
        try
        {
            var context = BuildMessageContext(ea);
            var success = await _dispatcher.DispatchAsync(ea.Body.ToArray(), context, cancellationToken);
            
            if (!_options.AutoAck)
            {
                if (success)
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                }
                else
                {
                    // No handler found - reject without requeue
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message '{MessageId}'", messageId);
            
            if (!_options.AutoAck)
            {
                // Requeue for retry
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
            }
        }
    }
    
    private MessageContext BuildMessageContext(BasicDeliverEventArgs ea)
    {
        var headers = new Dictionary<string, string>();
        if (ea.BasicProperties.Headers != null)
        {
            foreach (var header in ea.BasicProperties.Headers)
            {
                if (header.Value is byte[] bytes)
                    headers[header.Key] = Encoding.UTF8.GetString(bytes);
                else
                    headers[header.Key] = header.Value?.ToString() ?? "";
            }
        }
        
        // Try to get CloudEvents attributes from headers (binary mode) or properties
        var id = headers.GetValueOrDefault(CloudEventsConstants.IdHeader) 
                 ?? ea.BasicProperties.MessageId 
                 ?? Guid.NewGuid().ToString();
        var type = headers.GetValueOrDefault(CloudEventsConstants.TypeHeader)
                   ?? ea.BasicProperties.Type
                   ?? "";
        var source = headers.GetValueOrDefault(CloudEventsConstants.SourceHeader) ?? "/";
        
        DateTimeOffset? time = null;
        if (headers.TryGetValue(CloudEventsConstants.TimeHeader, out var timeStr))
        {
            DateTimeOffset.TryParse(timeStr, out var parsed);
            time = parsed;
        }
        
        return new MessageContext
        {
            Id = id,
            Type = type,
            Source = source,
            Time = time,
            Headers = headers,
            RawBody = ea.Body.ToArray()
        };
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping RabbitMQ consumer");
        
        foreach (var channel in _channels)
        {
            await channel.CloseAsync(cancellationToken);
            channel.Dispose();
        }
        
        await base.StopAsync(cancellationToken);
    }
}
```

---

## Part 4: Builder Integration

### 4.1 Update CloudEventBusBuilder

Add to `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs`:

```csharp
public class CloudEventBusBuilder
{
    // ... existing fields ...
    internal MessageHandlerRegistry HandlerRegistry { get; } = new();
    
    /// <summary>
    /// Registers a message handler.
    /// </summary>
    public CloudEventBusBuilder AddHandler<TMessage, THandler>()
        where TMessage : notnull
        where THandler : class, IMessageHandler<TMessage>
    {
        var eventType = TypeRegistry.ResolveEventType(typeof(TMessage));
        if (string.IsNullOrEmpty(eventType))
        {
            throw new InvalidOperationException(
                $"Cannot register handler for {typeof(TMessage).Name}: " +
                "message type must be registered first via AddMessage<T>() or have [CloudEvent] attribute.");
        }
        
        HandlerRegistry.Register<TMessage, THandler>(eventType);
        Services.AddScoped<THandler>();
        Services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());
        
        return this;
    }
    
    /// <summary>
    /// Registers a message with its handler in one call.
    /// </summary>
    public CloudEventBusBuilder AddMessageWithHandler<TMessage, THandler>(string eventType)
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        AddMessage<TMessage>(eventType);
        
        HandlerRegistry.Register<TMessage, THandler>(eventType);
        Services.AddScoped<THandler>();
        Services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());
        
        return this;
    }
}
```

### 4.2 Create RabbitMQ Consumer Builder

Add to `src/Saithis.CloudEventBus.RabbitMq/RabbitMqServiceCollectionExtensions.cs`:

```csharp
public static class RabbitMqServiceCollectionExtensions
{
    // ... existing AddRabbitMqMessageSender ...
    
    /// <summary>
    /// Adds RabbitMQ message consuming support.
    /// </summary>
    public static IServiceCollection AddRabbitMqConsumer(
        this IServiceCollection services,
        Action<RabbitMqConsumerOptions> configure)
    {
        var options = new RabbitMqConsumerOptions();
        configure(options);
        
        services.AddSingleton(options);
        services.AddSingleton<MessageDispatcher>();
        services.AddHostedService<RabbitMqConsumer>();
        
        return services;
    }
}
```

---

## Example Usage

### Define Handler

```csharp
public class NoteAddedHandler : IMessageHandler<NoteAddedEvent>
{
    private readonly ILogger<NoteAddedHandler> _logger;
    
    public NoteAddedHandler(ILogger<NoteAddedHandler> logger)
    {
        _logger = logger;
    }
    
    public Task HandleAsync(NoteAddedEvent message, MessageContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Note added: {Id} - {Text}", message.Id, message.Text);
        return Task.CompletedTask;
    }
}
```

### Register Handler

```csharp
services.AddCloudEventBus(bus => bus
    .AddMessage<NoteAddedEvent>("com.example.notes.added")
    .AddHandler<NoteAddedEvent, NoteAddedHandler>());

services.AddRabbitMqConsumer(consumer =>
{
    consumer.PrefetchCount = 20;
    consumer.Queues.Add(new QueueConsumerConfig
    {
        QueueName = "notes-queue"
    });
});
```

---

## Files to Create

1. `src/Saithis.CloudEventBus/Core/IMessageHandler.cs`
2. `src/Saithis.CloudEventBus/Core/IMessageDeserializer.cs`
3. `src/Saithis.CloudEventBus/Core/MessageHandlerRegistry.cs`
4. `src/Saithis.CloudEventBus/Core/MessageDispatcher.cs`
5. `src/Saithis.CloudEventBus/Serializers/Json/JsonMessageDeserializer.cs`
6. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqConsumerOptions.cs`
7. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqConsumer.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus/CloudEventBusBuilder.cs` - Add handler registration
2. `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs` - Register deserializer
3. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqServiceCollectionExtensions.cs` - Add consumer registration

---

## Testing Considerations

- Unit test handler registration
- Unit test message dispatching
- Unit test CloudEvents deserialization (both structured and binary)
- Integration test with RabbitMQ consuming real messages
- Test error handling and nack behavior
- Test handler DI scoping (scoped services per message)

## Future Enhancements

- Dead letter queue handling
- Message filtering by event type
- Batch message handling
- Parallel message processing
- Consumer health checks
