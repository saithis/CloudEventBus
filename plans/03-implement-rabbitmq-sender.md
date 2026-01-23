# Plan 03: Implement RabbitMQ Sender

## Priority: 3 (Core Functionality)

## Problem

The `RabbitMqMessageSender` is just a placeholder that throws `NotImplementedException`. Without a working sender, the library cannot actually send messages.

### Current State

```csharp
public class RabbitMqMessageSender : IMessageSender
{
    public Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
```

## Solution

Implement a proper RabbitMQ sender using the RabbitMQ.Client 7.x API with:
- Connection management with automatic reconnection
- Channel pooling for performance
- Proper async publish with confirmations
- Exchange/routing key resolution from message properties

### 1. Create RabbitMQ Configuration Options

Create `RabbitMqOptions.cs`:

```csharp
namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqOptions
{
    /// <summary>
    /// RabbitMQ connection string (e.g., "amqp://guest:guest@localhost:5672/")
    /// </summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>
    /// Alternative to ConnectionString - individual settings
    /// </summary>
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    
    /// <summary>
    /// Default exchange to publish to if not specified in MessageProperties.Extensions
    /// </summary>
    public string DefaultExchange { get; set; } = "";
    
    /// <summary>
    /// Whether to wait for publisher confirms
    /// </summary>
    public bool UsePublisherConfirms { get; set; } = true;
    
    /// <summary>
    /// Timeout for publisher confirms
    /// </summary>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

### 2. Create Connection Factory Wrapper

Create `RabbitMqConnectionManager.cs`:

```csharp
using RabbitMQ.Client;

namespace Saithis.CloudEventBus.RabbitMq;

internal class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    
    public RabbitMqConnectionManager(RabbitMqOptions options)
    {
        _options = options;
    }
    
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetOrCreateConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }
    
    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;
            
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;
                
            var factory = CreateConnectionFactory();
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }
    
    private ConnectionFactory CreateConnectionFactory()
    {
        if (!string.IsNullOrEmpty(_options.ConnectionString))
        {
            return new ConnectionFactory { Uri = new Uri(_options.ConnectionString) };
        }
        
        return new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
        };
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
        _connectionLock.Dispose();
    }
}
```

### 3. Implement the Message Sender

Update `RabbitMqMessageSender.cs`:

```csharp
using System.Text;
using RabbitMQ.Client;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public class RabbitMqMessageSender : IMessageSender, IAsyncDisposable
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;
    
    // Extension keys for routing
    public const string ExchangeExtensionKey = "rabbitmq.exchange";
    public const string RoutingKeyExtensionKey = "rabbitmq.routingKey";
    
    public RabbitMqMessageSender(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
    {
        _connectionManager = connectionManager;
        _options = options;
    }
    
    public async Task SendAsync(byte[] content, MessageProperties props, CancellationToken cancellationToken)
    {
        await using var channel = await _connectionManager.CreateChannelAsync(cancellationToken);
        
        if (_options.UsePublisherConfirms)
        {
            await channel.ConfirmSelectAsync(cancellationToken: cancellationToken);
        }
        
        var exchange = GetExchange(props);
        var routingKey = GetRoutingKey(props);
        var basicProps = CreateBasicProperties(props);
        
        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: basicProps,
            body: content,
            cancellationToken: cancellationToken);
            
        if (_options.UsePublisherConfirms)
        {
            await channel.WaitForConfirmsOrDieAsync(cancellationToken);
        }
    }
    
    private string GetExchange(MessageProperties props)
    {
        if (props.Extensions.TryGetValue(ExchangeExtensionKey, out var exchange))
            return exchange;
        return _options.DefaultExchange;
    }
    
    private string GetRoutingKey(MessageProperties props)
    {
        if (props.Extensions.TryGetValue(RoutingKeyExtensionKey, out var routingKey))
            return routingKey;
        // Fall back to message type if available
        return props.Type ?? "";
    }
    
    private static BasicProperties CreateBasicProperties(MessageProperties props)
    {
        var basicProps = new BasicProperties
        {
            ContentType = props.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };
        
        if (props.Headers.Count > 0)
        {
            basicProps.Headers = new Dictionary<string, object?>();
            foreach (var header in props.Headers)
            {
                basicProps.Headers[header.Key] = header.Value;
            }
        }
        
        if (!string.IsNullOrEmpty(props.Type))
        {
            basicProps.Type = props.Type;
        }
        
        return basicProps;
    }
    
    public async ValueTask DisposeAsync()
    {
        await _connectionManager.DisposeAsync();
    }
}
```

### 4. Create DI Extension Methods

Create `RabbitMqServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saithis.CloudEventBus.Core;

namespace Saithis.CloudEventBus.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessageSender(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null)
    {
        var options = new RabbitMqOptions();
        configure?.Invoke(options);
        
        services.AddSingleton(options);
        services.AddSingleton<RabbitMqConnectionManager>();
        services.Replace(ServiceDescriptor.Singleton<IMessageSender, RabbitMqMessageSender>());
        
        return services;
    }
}
```

### 5. Update Example Usage

In `PlaygroundApi/Program.cs`:

```csharp
builder.Services.AddMessageBus();
builder.Services.AddRabbitMqMessageSender(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("RabbitMq");
    // Or:
    // options.HostName = "localhost";
    // options.UserName = "guest";
    // options.Password = "guest";
});
```

Publishing with routing:

```csharp
db.OutboxMessages.Add(new NoteAddedEvent { ... }, new MessageProperties
{
    Type = "note.added",
    Extensions = 
    {
        [RabbitMqMessageSender.ExchangeExtensionKey] = "notes-exchange",
        [RabbitMqMessageSender.RoutingKeyExtensionKey] = "notes.added"
    }
});
```

---

## Files to Create

1. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqOptions.cs`
2. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqConnectionManager.cs`
3. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqServiceCollectionExtensions.cs`

## Files to Modify

1. `src/Saithis.CloudEventBus.RabbitMq/RabbitMqMessageSender.cs` - Full implementation

## Dependencies

The project already has `RabbitMQ.Client` version 7.1.2. Need to add:
- `Microsoft.Extensions.DependencyInjection.Abstractions` for the extension methods

Update `Saithis.CloudEventBus.RabbitMq.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="RabbitMQ.Client" Version="7.1.2" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
</ItemGroup>
```

---

## Testing Considerations

- Integration test with RabbitMQ container (TestContainers)
- Test connection recovery after network failure
- Test publisher confirms work correctly
- Test message properties are correctly mapped to BasicProperties
- Test routing key fallback behavior

## Future Enhancements (Not in this plan)

- Channel pooling for high-throughput scenarios
- Batch publishing support
- Dead letter exchange configuration
- Connection/channel health checks
- OpenTelemetry integration
