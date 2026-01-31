# Plan 11: Auto-provision Exchanges, Bindings, and Methods

## Priority: Feature Enhancement

## Problem

1.  **Topology Configuration**: Currently requires manual setup of exchanges and bindings.
2.  **Coupling**: Configuration is tightly coupled to RabbitMQ concepts (Exchanges/Bindings) in the public API, making it hard to switch generic application logic to other brokers (e.g., Kafka).
3.  **Fragmented Configuration**: Core setup and Transport setup are split across different method calls (`AddCloudEventBus` vs `AddRabbitMq...`).
4.  **Ownership Ambiguity**: It's unclear who owns the infrastructure (Exchange/Topic) - the producer or the consumer.
5.  **Routing Ambiguity**: No clear link between C# Message Type and valid destination/subscription.

## Solution

Introduce a **Transport-Agnostic, Intent-Based Fluent API** that:

1.  **Centralizes Configuration**: All configuration occurs within a single `AddCloudEventBus(builder => ...)` block.
2.  **Enforces Ownership**: Uses explicit intent methods (`ProducesEvent`, `SendsCommand`, `ConsumesEvent`, `ConsumesCommand`) to define strict ownership rules.
3.  **Extends for Transports**: Uses the "Options Pattern" and Extension Methods to attach transport-specific metadata (e.g., RabbitMQ Exchanges, Routing Keys).
4.  **Generic Registries**: `Saithis.CloudEventBus` holds the registries, while transports read from them.
5.  **Fails Fast**: Validates topological dependencies on startup.

**Note**: Backwards compatibility is NOT required. We will replace the existing configuration.

### Ownership Rules (Enforced by API)

| Intent | Use Case | Ownership (e.g. RabbitMQ Exchange) | Connection |
| :--- | :--- | :--- | :--- |
| **ProducesEvent** | We publish `OrderCreated` | **Us** (Declare) | `Message` -> `My Exchange` |
| **ConsumesCommand** | We receive `CreateOrder` | **Us** (Declare) | `My Exchange` -> `My Queue` |
| **ConsumesEvent** | We listen to `UserRegistered` | **Them** (Validate) | `Their Exchange` -> `My Queue` |
| **SendsCommand** | We send `ProcessPayment` | **Them** (Validate) | `Message` -> `Their Exchange` |

## API Design: Unified & Explicit

Everything happens inside `AddCloudEventBus`. The methods chosen dictate the default topology behavior (Declare vs Validate).

### Configuration Example

```csharp
services.AddCloudEventBus(builder => 
{
    builder.WithServiceName("OrderService");
    
    // Transport Configuration
    builder.UseRabbitMq(mq => mq.ConnectionString("amqp://..."));

    // Handlers (Generic)
    builder.AddHandler<CreateOrderHandler>();
    builder.AddHandler<UserRegisteredHandler>();

    // ==========================================
    // 1. PRODUCER: Events we own
    // ==========================================
    // Intent: We govern "orders.events". We declare it.
    builder.ProducesEvent<OrderCreated>("orders.events") // "orders.events" is the logical channel/exchange
           .WithRabbitMq(cfg => cfg
               .ExchangeType(ExchangeType.Topic)               
               .RoutingKey("order.created")
           );

    // ==========================================
    // 2. SENDER: Commands to others
    // ==========================================
    // Intent: We send to "payments.commands". We expect it to exist.
    builder.SendsCommand<ProcessPayment>("payments.commands")
           .WithRabbitMq(cfg => cfg
               .RoutingKey("cmd.pay")
           );

    // ==========================================
    // 3. CONSUMER: Commands we own
    // ==========================================
    // Intent: We own "orders.commands". We declare it and our queue.
    builder.ConsumesCommand<CreateOrder>(queue: "orders.process")
           .FromChannel("orders.commands") // The channel/exchange we expect to receive from
           .WithRabbitMq(cfg => cfg
                .ExchangeType(ExchangeType.Direct)
                .RoutingKey("cmd.create")
           );

    // ==========================================
    // 4. CONSUMER: Events from others
    // ==========================================
    // Intent: We listen to "users.events". We expect it to exist. We declare our queue.
    builder.ConsumesEvent<UserRegistered>(queue: "orders.user-handler")
           .FromChannel("users.events")
           .WithRabbitMq(cfg => cfg
                .RoutingKey("user.registered")
           );
});
```

### Metadata Extensions Implementation

The Core package doesn't refer to RabbitMQ. It provides a property bag for extensions:

```csharp
// In Saithis.CloudEventBus
public class ProductionRegistration
{
    public MessageIntent Intent { get; }
    public string ChannelName { get; }
    public Dictionary<string, object> Metadata { get; } = new();
}

// In Saithis.CloudEventBus.RabbitMq
public static class RabbitMqProductionExtensions
{
    public static ProductionBuilder<T> WithRabbitMq<T>(
        this ProductionBuilder<T> builder,
        Action<RabbitMqProductionOptions> configure)
    {
        var options = new RabbitMqProductionOptions();
        configure(options);
        builder.Registration.Metadata["RabbitMq"] = options;
        return builder;
    }
}
```

## Runtime Logic & Metadata

### Registry Models

The generic registry now captures the **Intent**:

```csharp
public enum MessageIntent { EventProduction, CommandSending, CommandConsumption, EventConsumption }

public class ProductionRegistration 
{
    public MessageIntent Intent { get; } // EventProduction vs CommandSending
    public string ChannelName { get; } // "orders.events"
    public Dictionary<string, object> Metadata { get; }
}

public class ConsumptionRegistration
{
    public MessageIntent Intent { get; } // CommandConsumption vs EventConsumption
    public string ChannelName { get; } 
    public string SubscriptionName { get; } // Queue name
    public Dictionary<string, object> Metadata { get; }
}
```

### Producer Correlation

Resolving the target exchange and routing key happens at the entry points, before the message reaches the sender.

**Entry Point 1: Direct Publication**
When `CloudEventBus.PublishDirectAsync(message)` is called:
1.  Bus looks up `T` (e.g., `OrderCreated`) in `ProductionRegistry`.
2.  Finds Target Exchange and Routing Key from metadata.
3.  Calls `IMessageSender.SendAsync(message, props)` with resolved properties.

**Entry Point 2: Transactional Outbox**
When `OutboxTriggerInterceptor.SavingChangesAsync` intercepts a save:
1.  Interceptor looks up `T` in `ProductionRegistry` for each outbox message.
2.  Finds Target Exchange and Routing Key from metadata.
3.  Stores them in the outbox record so the background processor can simply publish them later.

### Consumer Correlation (Dispatcher)

When a message arrives at `RabbitMqConsumer`:
1.  CloudEvents Envelope contains `type` attribute (populated from Routing Key or explicit type).
2.  **RabbitMQ Options** maps `type` string (e.g., "user.registered") to **Message Type** `UserRegistered` via `ConsumptionRegistry`.
    *   *Note: This is now decoupled from the Handler Type.*
3.  Dispatcher deserializes body to `UserRegistered`.
4.  Dispatcher resolves `IEnumerable<IMessageHandler<UserRegistered>>` from the Service Provider (registered via `AddHandler`).
5.  Dispatcher invokes `HandleAsync` on all found handlers.

### RabbitMQ Topology Manager

The `RabbitMqTopologyManager` uses the `Intent` to decide whether to `ExchangeDeclare` or `ExchangeDeclarePassive`.

On Startup:
1.  Iterate `ProductionRegistry`:
    *   Check `TransportMetadata["RabbitMq"]`.
    *   If `EventProduction` or `CommandConsumption`: **Declare** Exchange (it's ours).
    *   If `CommandSending` or `EventConsumption`: **Validate** Exchange exists (it's theirs).
2.  Iterate `ConsumptionRegistry`:
    *   Check `TransportMetadata["RabbitMq"]`.
    *   **Declare** Queue (always ours).
    *   Create bindings between Exchange and Queue.

### RabbitMQ Message Sender

When sending a message:
1.  Look up `T` in `ProductionRegistry`.
2.  Get `ProductionRegistration<T>`.
3.  Check `Metadata["RabbitMq"]`:
    *   **Found**: Use configured Exchange/RoutingKey.
    *   **Not Found**: Use fallback to defaults (e.g., default exchange, ChannelName as routing key).

### Fail-Early Strategy

1.  **Topology Validation**: Verify "Expected" exchanges exist (for `ConsumesEvent` and `SendsCommand`).
2.  **Type Validation**: Ensure configured message types are unique (can't map same type to two destinations without explicit overrides).
3.  **Handler Validation**: Warn if a message type is mapped for consumption but no handler is registered.
4.  **Metadata Validation**: Ensure required RabbitMQ metadata (exchange, routing key) is present for each registration.

## Implementation Plan

### Phase 1: Core Definitions (Saithis.CloudEventBus)

1.  **Refactor `CloudEventBusBuilder`**:
    *   Add `ProductionRegistry` and `ConsumptionRegistry`.
    *   Add `ProducesEvent<T>(string channelName)` method returning `ProductionBuilder<T>`.
    *   Add `SendsCommand<T>(string channelName)` method returning `ProductionBuilder<T>`.
    *   Add `ConsumesCommand<T>(string queueName)` method returning `ConsumptionBuilder<T>`.
    *   Add `ConsumesEvent<T>(string queueName)` method returning `ConsumptionBuilder<T>`.
    *   Store `Intent` in registries.
2.  **Define Registry Models**:
    *   `ProductionRegistration` (MessageType, Intent, ChannelName, MetadataDict).
    *   `ConsumptionRegistration` (MessageType, Intent, ChannelName, SubscriptionName, MetadataDict).
3.  **Enhance Handler Registration**:
    *   Expose `builder.AddHandler<THandler>()` which registers `THandler` as `IMessageHandler<TMessage>` in DI.
    *   Currently `MessageHandlerRegistry` manages `MessageType` -> `List<HandlerType>`.

### Phase 2: RabbitMQ Extensions (Saithis.CloudEventBus.RabbitMq)

1.  **Extension Methods**:
    *   `UseRabbitMq(...)` on `CloudEventBusBuilder`.
    *   `WithRabbitMq(...)` on `ProductionBuilder<T>`.
    *   `WithRabbitMq(...)` on `ConsumptionBuilder<T>`.
2.  **Options Configuration**:
    *   Define `RabbitMqProductionOptions` (ExchangeName, ExchangeType, RoutingKey, etc.).
    *   Define `RabbitMqConsumptionOptions` (Bindings, Prefetch, etc.).
3.  **Topology Manager**:
    *   Implement `RabbitMqTopologyManager`.
    *   Implement logic: `if (intent == EventProduction || intent == CommandConsumption) DeclareExchange else ValidateExchange`.

### Phase 3: Runtime Updates

1.  **Update `CloudEventBus`**: 
    *   Use `ProductionRegistry` to determine exchange/routing key in `PublishDirectAsync`.
2.  **Update `OutboxTriggerInterceptor`**: 
    *   Use `ProductionRegistry` to determine exchange/routing key in `SavingChangesAsync`.
3.  **Update `RabbitMqMessageSender`**:
    *   Inject `ProductionRegistry`.
    *   Use registry to resolve destination before publishing.
4.  **Update `MessageDispatcher` / `RabbitMqConsumer`**:
    *   Use `ConsumptionRegistry` to resolve `string type` -> `Type messageType`.
    *   Deserialize message to resolved type.
    *   Resolve handlers from DI.

### Phase 4: Topology & Validation

1.  **`RabbitMqTopologyManager`**:
    *   Injects registries.
    *   Declares owned exchanges, queues, and bindings during startup.
    *   Validates expected exchanges exist.
2.  **Startup Validation**:
    *   Validate message type uniqueness.
    *   Warn on missing handlers for consumed message types.
    *   Validate required metadata is present.

## Verification Plan

### Automated Tests

#### Unit Tests (Core)
*   **Registry Tests**:
    *   Verify `ProductionRegistry` and `ConsumptionRegistry` populate correctly.
    *   Verify Metadata dictionary stores extension data.
    *   Verify Intent is stored correctly for each registration type.

#### Integration Tests (RabbitMq)
*   **Ownership Tests**:
    *   `ProducingEvent_DeclaresExchange`: Verify exchange created for `ProducesEvent`.
    *   `SendingCommand_ValidatesExchange`: Verify it throws if exchange missing for `SendsCommand`.
    *   `ConsumingCommand_DeclaresExchange`: Verify exchange and queue created for `ConsumesCommand`.
    *   `ConsumingEvent_ValidatesExchange`: Verify it throws if exchange missing for `ConsumesEvent`.
*   **Topology Tests**:
    *   `Topology_Provisions_Owned_Resources`: Configure a producer and consumer with specific RabbitMQ bindings. Start the app. Use `RabbitMQ.Client` to verify the Exchange and Queue exist and are bound.
    *   Verify `WithRabbitMq` options are respected by `TopologyManager` (Queues created with correct bindings).
*   **Runtime Tests**:
    *   `Dispatcher_WithDecoupledHandler_InvokesCorrectHandler`:
        *   Register `TestHandler` in Core via `AddHandler`.
        *   Configure consumption mapping in RabbitMQ.
        *   Publish message.
        *   Verify `TestHandler` called.
    *   `Sender_ResolvesDestination`: Verify Sender resolves Exchange/RoutingKey from registry.
    *   `MissingHandler_LogsWarning`: Configure consumption but don't register handler, verify warning.
*   **End-to-End Flow**:
    *   Configure `ProducesEvent<TestMessage>`.`WithRabbitMq(...)`.
    *   Configure `ConsumesEvent<TestMessage>`.`WithRabbitMq(...)`.
    *   Publish `TestMessage` via `PublishDirectAsync`.
    *   Verify handler receives it.

### Manual Verification
*   Run the Test Application with the new configuration.
*   Check RabbitMQ Management UI to see if Exchanges/Queues match the "Intent" defined in code.
*   Verify owned exchanges are declared and expected exchanges are validated.

## Files to Create/Modify

### Saithis.CloudEventBus
*   `MessageBusServiceCollectionExtensions.cs` - Enhance generic registration
*   `Configuration/CloudEventBusBuilder.cs` - Add intent methods and registries
*   `Configuration/ProductionBuilder.cs` - New, fluent builder for production config
*   `Configuration/ConsumptionBuilder.cs` - New, fluent builder for consumption config
*   `Configuration/ProductionRegistry.cs` - New, stores production registrations
*   `Configuration/ConsumptionRegistry.cs` - New, stores consumption registrations
*   `Configuration/MessageIntent.cs` - New, enum for intent types

### Saithis.CloudEventBus.RabbitMq
*   `ServiceCollectionExtensions.cs` - Update/Rewrite to use new API
*   `Configuration/RabbitMqProductionOptions.cs` - New, RabbitMQ-specific production config
*   `Configuration/RabbitMqConsumptionOptions.cs` - New, RabbitMQ-specific consumption config
*   `Configuration/RabbitMqExtensions.cs` - New, extension methods for `WithRabbitMq`
*   `Infrastructure/RabbitMqTopologyManager.cs` - New, handles topology provisioning
*   `RabbitMqMessageSender.cs` - Rewrite logic to use registry
*   `RabbitMqConsumer.cs` - Update to use consumption registry
*   `MessageDispatcher.cs` - Update to resolve types from registry
