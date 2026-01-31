# Plan 11: Auto-provision Exchanges, Bindings, and Methods

## Priority: Feature Enhancement

## Problem

1.  **Configuration Redundancy**: Previous designs required repeating exchange/channel details for every single message type, violating DRY.
2.  **Mental Model Mismatch**: Developers think in terms of "Channels" or "Pipes" first, then what flows through them. Configuring per-message obscures the topology.
3.  **Coupling**: Configuration is tightly coupled to RabbitMQ concepts in the public API.
4.  **Ownership Ambiguity**: It needs to be crystal clear who owns the infrastructure (Exchange/Topic) - the producer or the consumer.

## Solution

Introduce a **Channel-First, Intent-Based Fluent API** that:

1.  **Centralizes Topology**: You define a "Channel" (Exchange) once, applying transport settings (Type, Durability) to the Channel.
2.  **Groups Messages**: You attach multiple Message Types to a Channel.
3.  **Enforces Ownership**: Distinct methods for `EventPublish`, `CommandPublish`, `CommandConsume`, `EventConsume` enforce who declares vs who validates.
4.  **Fails Fast**: Validates topological dependencies on startup.

### Ownership Rules

| Intent | API Method | Ownership | Action (RabbitMQ) |
| :--- | :--- | :--- | :--- |
| **Produces Event** | `AddEventPublishChannel` | **Us** (Originator) | **Declare** Exchange (Topic) |
| **Sends Command** | `AddCommandPublishChannel` | **Them** (Receiver) | **Validate** Exchange Exists |
| **Consumes Command** | `AddCommandConsumeChannel` | **Us** (Processor) | **Declare** Exchange (Direct) + Queue |
| **Consumes Event** | `AddEventConsumeChannel` | **Them** (Publisher) | **Validate** Exchange + **Declare** Queue + **Bind** |

## API Design: Channel-First

Everything happens inside `AddCloudEventBus`.

### Configuration Example

```csharp
services.AddCloudEventBus(builder => 
{
    builder.WithServiceName("OrderService");
    
    // Transport Configuration (Global)
    builder.UseRabbitMq(mq => mq.ConnectionString("amqp://..."));

    // Handlers (Generic Registration)
    // Registers THandler as IMessageHandler<TMessage> in DI
    builder.AddHandler<CreateOrderHandler>();
    builder.AddHandler<UserRegisteredHandler>();

    // ==========================================
    // 1. PRODUCER: Events we own
    // Intent: We govern "orders.events". We declare it.
    // ==========================================
    builder.AddEventPublishChannel("orders.events") 
           .WithRabbitMq(cfg => cfg
               .ExchangeType(ExchangeType.Topic)               
           )
           // Default Routing Key: Uses [CloudEvent("type")] or typeof(T).Name
           .Produces<OrderCreated>() 
           // Overridden Routing Key
           .Produces<OrderCancelled>(cfg => cfg.WithRoutingKey("order.cancelled"));

    // ==========================================
    // 2. SENDER: Commands to others
    // Intent: We send to "payments.commands". We expect it to exist.
    // ==========================================
    builder.AddCommandPublishChannel("payments.commands") 
           // We validate it exists and matches expectations (e.g. is Topic/Direct as expected)
           .WithRabbitMq(cfg => cfg
               .ExchangeType(ExchangeType.Direct) 
           )
           .Sends<ProcessPayment>(cfg => cfg.WithRoutingKey("cmd.pay"));

    // ==========================================
    // 3. CONSUMER: Commands we own
    // Intent: We own "orders.commands". We declare it and our queue.
    // ==========================================
    builder.AddCommandConsumeChannel("orders.commands") 
           .WithRabbitMq(cfg => cfg
                .ExchangeType(ExchangeType.Direct)
                // For Command Consumption, we usually bind a specific queue
                .QueueName("orders.process") 
           )
           // Implicitly binds using Routing Key derived from Type or Attribute
           .Consumes<CreateOrder>(); 

    // ==========================================
    // 4. CONSUMER: Events from others
    // Intent: We listen to "users.events". We expect it to exist. We declare our queue.
    // ==========================================
    builder.AddEventConsumeChannel("users.events")
           .WithRabbitMq(cfg => cfg
                .QueueName("orders.user-handler") // The shared queue for this channel's subscriptions
                .ExchangeType(ExchangeType.Topic) // We expect this type
                // Optional: Global settings for this consumer (e.g. Prefetch)
            )
           // Binds queue "orders.user-handler" to exchange "users.events" with key "user.registered"
           .Consumes<UserRegistered>(cfg => cfg.WithRoutingKey("user.registered")); 
});
```

### Attributes

We rely on `[CloudEvent("type")]` to identify messages.

```csharp
[CloudEvent("order.created")]
public record OrderCreated(string OrderId);
```

If the attribute is missing, the framework defaults to `typeof(T).Name`.

## Registry Architecture

We will merge the concept of "Message Registry" and "Handler Registry" into a unified **Topology Registry**.

### 1. ChannelRegistry
The root container.
*   `Dictionary<string, ChannelRegistration> Channels`

### 2. ChannelRegistration
Represents a logical channel (Exchange).
*   `string ChannelName`
*   `ChannelType Intent` (EventPublish, CommandPublish, CommandConsume, EventConsume)
*   `Dictionary<string, object> Metadata` (Transport extensions, e.g., RabbitMqOptions)
*   `List<MessageRegistration> Messages`

### 3. MessageRegistration
Represents a message flowing through that channel.
*   `Type MessageType`
*   `string MessageTypeName` (The wire-level "type" attribute)
*   `Dictionary<string, object> Metadata` (Override routing keys, etc.)

## Runtime Implementation

### RabbitMQ Topology Manager
1.  **Iterate `ChannelRegistry`**:
    *   For `EventPublish` / `CommandConsume`: **Declare Exchange** (using metadata settings).
    *   For `CommandPublish` / `EventConsume`: **Validate Exchange** (Passive Declare).
    *   For `CommandConsume`: **Declare Queue** (from metadata) -> **Bind** to Exchange (RoutingKey = MessageType).
    *   For `EventConsume`: **Declare Queue** (from metadata) -> **Bind** to Exchange (RoutingKey = MessageType).

### Message Sender (Producer)
1.  **Lookup**: `ChannelRegistry.FindChannelForMessage<T>()`.
    *   *Constraint*: A message type `T` should optimally belong to only ONE Publish channel. If multiple, we need explicit selection or it throws.
2.  **Resolve**:
    *   Exchange = `Channel.Name`
    *   RoutingKey = `Message.Metadata["RoutingKey"]` ?? `Message.CloudEventType`
3.  **Publish**:
    *   `Map<T> -> bytes`
    *   `BasicPublish(Exchange, RoutingKey, Props, Body)`

### Message Dispatcher (Consumer)
1.  **Startup**:
    *   Iterate `ChannelRegistry` for Consumer channels.
    *   Start `RabbitMqConsumer` for the defined Queues.
2.  **Runtime**:
    *   Receive BasicDeliver.
    *   Extract `type` from header/routing key.
    *   **Lookup**: Find `Type` in `ChannelRegistry` where `Intent` is Consumer and `MessageTypeName` matches.
    *   **Deserialize**: `JsonSerializer.Deserialize(body, foundType)`.
    *   **Invoke**: Resolve `IEnumerable<IMessageHandler<foundType>>` and `HandleAsync`.

## Implementation Plan

### Phase 1: Core Abstractions (Saithis.CloudEventBus)
1.  **Create Registries**:
    *   `ChannelRegistry`, `ChannelRegistration`, `MessageRegistration`.
    *   `ChannelType` Enum.
2.  **Create Builders**:
    *   `ChannelBuilder` (Generic base or specific per intent).
    *   `MessageBuilder` (For per-message overrides).
3.  **Update `CloudEventBusBuilder`**:
    *   Add `AddEventPublishChannel`, `AddCommandPublishChannel`, etc.
    *   Remove old `Produces<T>` root methods.

### Phase 2: RabbitMQ Extensions (Saithis.CloudEventBus.RabbitMq)
1.  **Options Models**:
    *   `RabbitMqChannelOptions` (ExchangeType, Durable, AutoDelete).
    *   `RabbitMqConsumerOptions` (QueueName, Prefetch).
    *   `RabbitMqMessageOptions` (RoutingKey).
2.  **Extension Methods**:
    *   `WithRabbitMq(Action<RabbitMqChannelOptions>)` on Channel Builder.
    *   `WithRabbitMq(Action<RabbitMqConsumerOptions>)` on Consumer Channel Builder.
3.  **Topology Manager**:
    *   Rewrite to iterate `ChannelRegistry`.
    *   Implement Definition vs Validation logic.

### Phase 3: Runtime & Sender
1.  **Update Sender**:
    *   Inject `ChannelRegistry`.
    *   Logic: `registry.GetPublishChannel<T>()` -> Publish.
2.  **Update Consumer**:
    *   Inject `ChannelRegistry`.
    *   Build map of `string typeName` -> `Type clrType`.

## Verification

### Automated Tests
1.  **Unit**: Verify Registry builds correctly from Fluent API.
2.  **Integration (RabbitMQ)**:
    *   **Scenario 1: Full Lifecycle**:
        *   Service A: `AddEventPublishChannel("test.events").Produces<MyEvent>()`.
        *   Service B: `AddEventConsumeChannel("test.events").Consumes<MyEvent>()`.
        *   Assert: Exchange "test.events" declared. Queue created. Binding created. Message flows.
    *   **Scenario 2: Validation Failure**:
        *   Service A: `AddCommandPublishChannel("non.existent")`.
        *   Assert: Application throws Triggering Exception on startup (Exchange not found).

### Manual Check
*   Run `Saithis.CloudEventBus.Test`.
*   Check RabbitMQ Management for correct Exchange Types and Bindings.
