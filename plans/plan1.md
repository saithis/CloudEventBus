# Plan 11: Auto-provision Exchanges, Bindings, and Methods

## Priority: Feature Enhancement

## Problem

1.  **Topology Configuration**: Currently requires manual setup of exchanges and bindings.
2.  **Routing Ambiguity**: There is no clear link between a C# Message Type (e.g., `OrderCreated`) and its destination Exchange/Routing Key.
3.  **Ownership Confusion**: Ownership of exchanges (who declares lines?) is not enforced.
4.  **Dispatcher Configuration**: Consumers need to know which C# class to deserialize to and which handler to invoke.

## Solution

Introduce a **Unified, Intent-Based Fluent API** that:
1.  **Defines Topology**: Declares exchanges/queues based on strict service ownership.
2.  **Maps Messages**: Explicitly correlates Message Types to Exchanges and Routing Keys.
3.  **Decouples Handlers**: Handlers are registered in the core generic container, independent of the transport (RabbitMQ/Kafka).
4.  **Maps Consumption**: RabbitMQ configuration maps Routing Keys to Message Types.
5.  **Fails Fast**: Validates topological dependencies on startup.

**Note**: Backwards compatibility is NOT required. We will replace the existing configuration.

### Ownership Rules (Enforced by API)

| Intent | Use Case | Exchange Ownership | Connection |
| :--- | :--- | :--- | :--- |
| **Produces Events** | We publish `OrderCreated` | **Us** (Declare) | `Message<T>` -> `Exchange` |
| **Consumes Commands** | We receive `CreateOrder` | **Us** (Declare) | `Exchange` -> `Queue` |
| **Consumes Events** | We listen to `UserRegistered` | **Them** (Validate) | `Their Exchange` -> `Queue` |
| **Sends Commands** | We send `ProcessPayment` | **Them** (Validate) | `Message<T>` -> `Their Exchange` |

## API Design: Unified & Typed

We replace `AddRabbitMqMessageSender`/`AddRabbitMqConsumer` with `AddCloudEventBus` (Core) and `AddCloudEventBusRabbitMq` (Transport).

### Configuration Example

```csharp
// 1. Core Registration (Handlers & Common Services)
// This is agnostic of the underlying broker (RabbitMQ, Kafka, etc.)
services.AddCloudEventBus(builder => 
{
    // Register handlers. The builder scans THandler for IMessageHandler<T> interfaces.
    builder.AddHandler<CreateOrderHandler>(); 
    builder.AddHandler<UserRegisteredHandler>();
});

// 2. RabbitMQ Transport Configuration
services.AddCloudEventBusRabbitMq(builder =>
{
    builder.Connection("amqp://guest:guest@localhost:5672");

    // PRODUCER: Events we own (Topology + Routing)
    builder.ProducesEvents(exchange: "orders.events", type: ExchangeType.Topic)
           .WithMessageType<OrderCreated>(routingKey: "order.created")
           .WithMessageType<OrderCancelled>(routingKey: "order.cancelled");

    // CONSUMER: Commands we own (Topology + Routing + Valid types)
    builder.ConsumesCommands(exchange: "orders.commands", queue: "orders.process")
           .WithRoutingKey("cmd.create") // Binds queue to exchange
           .MapToMessage<CreateOrder>("cmd.create"); // incoming "cmd.create" -> CreateOrder type

    // CONSUMER: Events from others (Topology + Valid types)
    builder.ConsumesEvents(fromExchange: "users.events", intoQueue: "orders.user-handler")
           .WithRoutingKeys("user.registered")
           .MapToMessage<UserRegistered>("user.registered");

    // SENDER: Commands to others (Routing)
    builder.SendsCommands(toExchange: "payments.commands")
           .WithMessageType<ProcessPayment>(routingKey: "cmd.pay");
});
```

### Producer Correlation

Resolving the target exchange and routing key happens at the entry points, before the message reaches the sender.

**Entry Point 1: Direct Publication**
When `CloudEventBus.PublishDirectAsync(message)` is called:
1.  Bus looks up `T` (e.g., `OrderCreated`) in configuration.
2.  Finds Target Exchange and Routing Key.
3.  Calls `IMessageSender.SendAsync(message, props)`.

**Entry Point 2: Transactional Outbox**
When `OutboxTriggerInterceptor.SavingChangesAsync` intercepts a save:
1.  Interceptor looks up `T` in configuration for each outbox message.
2.  Finds Target Exchange and Routing Key.
3.  Stores them in the outbox record so the background processor can simply publish them later.

### Consumer Correlation (Dispatcher)

When a message arrives at `RabbitMqConsumer`:
1.  CloudEvents Envelope contains `type` attribute (populated from Routing Key or explicit type).
2.  **RabbitMQ Options** maps `type` string (e.g. "cmd.create") to **Message Type** `CreateOrder`.
    *   *Note: This is now decoupled from the Handler Type.*
3.  Dispatcher deserializes body to `CreateOrder`.
4.  Dispatcher resolves `IEnumerable<IMessageHandler<CreateOrder>>` from the Service Provider (registered via `AddCloudEventBus`).
5.  Dispatcher invokes `HandleAsync` on all found handlers.

### Fail-Early Strategy

1.  **Topology Validation**: Verify "Expected" exchanges exist.
2.  **Type Validation**: Ensure configured message types are unique (can't map same type to two destinations without explicit overrides).
3.  **Handler Validation**: Warn if a message type is mapped for consumption but no handler is registered.

## Implementation Plan

### Phase 1: Core Updates (`Saithis.CloudEventBus`)

1.  **`AddCloudEventBus(Action<CloudEventBusBuilder> configure)`**:
    *   Setup the core `MessageHandlerRegistry` (or rely on pure DI).
    *   Currently `MessageHandlerRegistry` manages `MessageType` -> `List<HandlerType>`.
    *   Expose `builder.AddHandler<THandler>()` which registers `THandler` as `IMessageHandler<TMessage>` in DI.

### Phase 2: Configuration & Registries (`Saithis.CloudEventBus.RabbitMq`)

1.  **Create `RabbitMqUnifiedOptions`**:
    *   Stores topology definitions.
    *   Stores `ProductionRegistry` (Type -> Exchange/RoutingKey).
    *   Stores `ConsumptionRegistry` (String Type -> MessageType).

### Phase 3: Topology Manager

1.  **Implement `RabbitMqTopologyManager`**:
    *   Provisions/Validates topology on startup based on Options.

### Phase 4: Fluent Builder

1.  Implement the fluent API to populate options.
    *   `MapToMessage<TMessage>(string typeName)` replaces `HandleMessage`.
    *   Ensures `TMessage` is a valid message type.

### Phase 5: Runtime Logic

1.  **Update `CloudEventBus`**: Use `ProductionRegistry` to determine exchange/routing key in `PublishDirectAsync`.
2.  **Update `OutboxTriggerInterceptor`**: Use `ProductionRegistry` to determine exchange/routing key in `SavingChangesAsync`.
3.  **Update `MessageDispatcher` / `RabbitMqConsumer`**: 
    *   Use `ConsumptionRegistry` to resolve `string type` -> `Type messageType`.
    *   Deserialize message.
    *   Resolve handlers from DI.

## Testing Strategy

*   **Integration Tests**:
    *   `Dispatcher_WithDecoupledHandler_InvokesCorrectHandler`:
        *   Register `TestHandler` in Core.
        *   Configure `test.msg` -> `TestMessage` in RabbitMQ.
        *   Publish message with type `test.msg`.
        *   Verify `TestHandler` called.
    *   `Topology_Provisions_Owned_Resources`: Verify exchanges/queues from config.
    *   `MissingHandler_LogsWarning`: Configure consumption but don't register handler.

## Files to Create/Modify
- `src/Saithis.CloudEventBus/MessageBusServiceCollectionExtensions.cs` (Enhance generic registration)
- `src/Saithis.CloudEventBus.RabbitMq/ServiceCollectionExtensions.cs` (Replace/Rewrite)
- `src/Saithis.CloudEventBus.RabbitMq/Configuration/RabbitMqUnifiedOptions.cs` (New)
- `src/Saithis.CloudEventBus.RabbitMq/Configuration/RabbitMqFluentBuilder.cs` (New)
- `src/Saithis.CloudEventBus.RabbitMq/Infrastructure/RabbitMqTopologyManager.cs` (New)
- `src/Saithis.CloudEventBus.RabbitMq/RabbitMqMessageSender.cs` (Rewrite logic)
