

# Config thoughts

## Declare topology

Extend auto-provisioning to support exchange declaration and queue-to-exchange bindings based on **ownership boundaries**:

- **Publisher (events we own)**: Declare exchanges in `RabbitMqOptions` - others bind to these
- **Consumer (commands we own)**: Declare exchanges in `RabbitMqConsumerOptions` - others publish to these
- **Consumer (events from others)**: Bind queues to their exchanges (don't declare them)
- **Publisher (commands to others)**: Do nothing - they own the exchange

This ensures each service only manages infrastructure it owns, following microservices best practices.

## High level API


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
    // (One Exchange, Multiple Messages)==========================================
    // Intent: We govern "orders.events". We declare it.
    builder.AddEventPublishChannel("orders.events") // "orders.events" is the logical channel/exchange
           .WithRabbitMq(cfg => cfg
               .ExchangeType(ExchangeType.Topic)               
           )
           .Produces<OrderCreated>(cfg => cfg.RoutingKey("order.created"))
           .Produces<OrderCancelled>(); // Uses [CloudEvent] attribute or default

    // ==========================================
    // 2. SENDER: Commands to others
    // (External Exchange)==========================================
    // Intent: We send to "payments.commands". We expect it to exist.
    builder.AddCommandPublishChannel("payments.commands") // "payments.commands" is the logical channel/exchange
           .WithRabbitMq(cfg => cfg
               .ExchangeType(ExchangeType.Topic) // do we even need this? We just need to make sure an echange with this name exists, we don't care about the settings.              
           )
           .AddMessage<ProcessPayment>(cfg => cfg.RoutingKey("cmd.pay"));

    // ==========================================
    // 3. CONSUMER: Commands we own
    // (Our Exchange -> Our Queue)==========================================
    // Intent: We own "orders.commands". We declare it and our queue.
    builder.AddCommandConsumeChannel("orders.commands") // The Logical Channel/sxchange
           .WithRabbitMq(cfg => cfg
                .ExchangeType(ExchangeType.Direct)
                .Queue("orders.process")
           )
           .Consumes<CreateOrder>(); // Implicitly binds with routing key derived from type

    // ==========================================
    // 4. CONSUMER: Events from others
    // (External Exchange -> Our Queue)==========================================
    // Intent: We listen to "users.events". We expect it to exist. We declare our queue.

    builder.AddEventConsumeChannel("users.events")
           .WithRabbitMq(cfg => cfg
                .Queue("orders.user-handler") // The queue to create/bind
                .RoutingKey("user.registered") // Default routing key for this block?
            )
           .Consumes<UserRegistered>(); 
});
```

## Message attribute

```[CloudEvent("type")]```

Used to know type for publishand to find type for receive.

## Other

* Backwards compatibility is NOT required, since this library is not in use yet.
* All config methods above should be in Saithis.CloudEventBus, except for WithRabbitMq and UseRabbitMq and their inner config methods.
* Combine MessageTypeRegistry and MessageHandlerRegistry into one and refactor the result to hold the information about messages, handlers and channels.
* The combined registry should have a way for the transport library (Saithis.CloudEventBus.RabbitMq) to register the transport specific information (exchange type, queue name, routing key, etc.). This should be generic, so that a possible future Saithis.CloudEventBus.Kafka can use the same registry.
* MessagePropertiesEnricher should be kept to enrich the MessageProperties with the information the transport needs.
* `CloudEventBus.PublishDirectAsync(message)` and `OutboxTriggerInterceptor.SavingChangesAsync` are the only entry points for message sending.
* Auto-provisioning should be idempotent and failsafe.
* Validates and creates the topological dependencies on startup and fails early if something is wrong. It checks at least:
       * Verify "Expected" exchanges exist (for event consume and command send).
       * Ensure configured message types are unique (can't map same type to two destinations without explicit overrides).
       * Warn if a message type is mapped for consumption but no handler is registered.
       * Ensure required RabbitMQ metadata (exchange, routing key) is present for each registration.
