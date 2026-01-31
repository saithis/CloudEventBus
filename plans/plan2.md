# Plan 11: Auto-provision Exchanges, Bindings, and Methods

## Priority: Feature Enhancement

## Problem

1.  **Topology Configuration**: Currently requires manual setup of exchanges and bindings.
2.  **Coupling**: Configuration is tightly coupled to RabbitMQ concepts (Exchanges/Bindings) in the public API, making it hard to switch generic application logic to other brokers (e.g., Kafka).
3.  **Fragmented Configuration**: Core setup and Transport setup are split across different method calls (`AddCloudEventBus` vs `AddRabbitMq...`).
4.  **Routing Ambiguity**: No clear link between C# Message Type and valid destination/subscription.

## Solution

Introduce a **Transport-Agnostic, Intent-Based Fluent API** that:

1.  **Centralizes Configuration**: All configuration occurs within a single `AddCloudEventBus(builder => ...)` block.
2.  **Abstracts Intent**: Uses generic terms (`Produces<T>`, `Consumes<T>`, `Destination`, `Subscription`) for the public API.
3.  **Extends for Transports**: Uses the "Options Pattern" and Extension Methods to attach transport-specific metadata (e.g., RabbitMQ Exchanges, Routing Keys) to the generic items.
4.  **Generic Registries**: `Saithis.CloudEventBus` holds the registries for Production and Consumption, while transports only read from them to configure themselves.

## API Design: Unified & Extensible

Everything happens inside `AddCloudEventBus`. Transport details are added via extension methods on the builder or the specific registration items.

### Configuration Example

```csharp
services.AddCloudEventBus(builder => 
{
    // ==========================================
    // 1. Core / Transport Setup
    // ==========================================
    builder.WithServiceName("OrderService");
    
    // Configure RabbitMQ as the transport. 
    // This extension method registers the RabbitMQ sender/consumer specific services.
    builder.UseRabbitMq(mq => 
    {
        mq.ConnectionString("amqp://guest:guest@localhost:5672");
    });

    // ==========================================
    // 2. Handlers (Generic)
    // ==========================================
    // Registers CreateOrderHandler as IMessageHandler<CreateOrder>
    builder.AddHandler<CreateOrderHandler>();
    builder.AddHandler<UserRegisteredHandler>();

    // ==========================================
    // 3. Producers (We send these)
    // ==========================================
    // API: builder.Produces<T>(string defaultKey)
    
    // Simple case: Uses default transport logic (e.g., Rabbit: Exchange=default, RoutingKey=order.created)
    builder.Produces<OrderCreated>("order.created");

    // Complex case: Transport-specific overrides
    builder.Produces<OrderCancelled>("order.cancelled")
           .WithRabbitMq(cfg => cfg
               .Exchange("orders.events", ExchangeType.Topic)
               .RoutingKey("order.cancelled") // Explicit override
           );

    // ==========================================
    // 4. Consumers (We listen to these)
    // ==========================================
    // API: builder.Consumes<T>(string subscriptionName)
    // subscriptionName maps to a Queue in RabbitMQ or ConsumerGroup/Topic in Kafka
    
    // Command Consumption (Own Exchange -> Own Queue)
    builder.Consumes<CreateOrder>("orders.process")
           .WithRabbitMq(cfg => cfg
                .Bind("orders.commands", "cmd.create") // Bind queue "orders.process" to exchange "orders.commands"
                .Prefetch(10)
           );

    // Event Consumption (Other's Exchange -> Own Queue)
    builder.Consumes<UserRegistered>("orders.user-handler")
           .WithRabbitMq(cfg => cfg
                .Bind("users.events", "user.registered")
           );
});
```

### Metadata Extensions approach

The Core package doesn't refer to RabbitMQ. It provides a property bag for extensions:

```csharp
// In Core
public class MessageProductionConfig<T>
{
    public string DefaultKey { get; }
    public Dictionary<string, object> TransportMetadata { get; } = new();
}

// In RabbitMQ Package
public static MessageProductionConfig<T> WithRabbitMq<T>(
    this MessageProductionConfig<T> config, 
    Action<RabbitMqProductionOptions> configure)
{
    var options = new RabbitMqProductionOptions();
    configure(options);
    config.TransportMetadata["RabbitMq"] = options;
    return config;
}
```

## Runtime Logic

### 1. Producer (RabbitMqMessageSender)

Previous flow: `SendAsync(msg)` -> properties provided manually.
New flow: `PublishDirectAsync(msg)` (or Outbox):
1.  Look up `T` in `ProductionRegistry`.
2.  Get `MessageProductionConfig<T>`.
3.  Check `TransportMetadata["RabbitMq"]`.
    *   **Found**: Use configured Exchange/RoutingKey.
    *   **Not Found**: Fallback to defaults (e.g. `DefaultKey` as routing key, default exchange).

### 2. Consumer / Topology (RabbitMqTopologyManager)

On Startup:
1.  Iterate `ConsumptionRegistry`.
2.  Check `TransportMetadata["RabbitMq"]`.
    *   For each binding defined, declare Queue and Bindings.
3.  Iterate `ProductionRegistry`.
    *   Declare Exchanges defined in metadata.

### 3. Dispatcher

1.  Consumer receives message.
2.  Resolves `RoutingKey` / `Type` header.
3.  Looks up `ConsumptionRegistry` to find the mapped logic message type (if needed) or dispatches based on the CLR type if the envelope contains it.

## Implementation Plan

### Phase 1: Core Definitions (Saithis.CloudEventBus)

1.  **Refactor `CloudEventBusBuilder`**:
    *   Add `ProductionRegistry` and `ConsumptionRegistry`.
    *   Add `Produces<T>(string defaultKey)` method.
    *   Add `Consumes<T>(string subscriptionId)` method.
2.  **Define Registry Models**:
    *   `ProductionRegistration` (Type, DefaultKey, MetadataDict).
    *   `ConsumptionRegistration` (Type, SubscriptionId, MetadataDict).

### Phase 2: RabbitMQ Extensions (Saithis.CloudEventBus.RabbitMq)

1.  **Extension Methods**:
    *   `UseRabbitMq(...)` on `CloudEventBusBuilder`.
    *   `WithRabbitMq(...)` on `ProductionBuilder`.
    *   `WithRabbitMq(...)` on `ConsumptionBuilder`.
2.  **Options Configuration**:
    *   Define `RabbitMqProductionOptions` (Exchange, RoutingKey, etc.).
    *   Define `RabbitMqConsumptionOptions` (Bindings, Prefetch, etc.).

### Phase 3: Topology & Runtime

1.  **`RabbitMqTopologyManager`**:
    *   Injects registries.
    *   Declares declared exchanges, queues, and bindings during startup.
2.  **Update `RabbitMqMessageSender`**:
    *   Inject `ProductionRegistry`.
    *   Use registry to resolve destination before publishing.

### Phase 4: Verification

1.  **Unit Tests (Core)**:
    *   Verify Registries populate correctly.
    *   Verify Metadata dictionary stores extension data.
2.  **Integration Tests (RabbitMq)**:
    *   Verify `WithRabbitMq` options are respected by TopologyManager (Queues created).
    *   Verify Sender resolves Exchange/RoutingKey from registry.

## Verification Plan

### Automated Tests
*   **Topology Test**: Configure a producer and consumer with specific RabbitMQ bindings. Start the app. Use `RabbitMQ.Client` to verify the Exchange and Queue exist and are bound.
*   **End-to-End Flow**:
    *   Configure `Produces<TestMessage>("key").WithRabbitMq(...)`.
    *   Configure `Consumes<TestMessage>("queue").WithRabbitMq(...)`.
    *   Publish `TestMessage`.
    *   Verify handler receives it.

### Manual Verification
*   Run the Test Application with the new configuration.
*   Check RabbitMQ Management UI to see if Exchanges/Queues match the "Intent" defined in code.
