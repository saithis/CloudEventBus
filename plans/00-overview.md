# CloudEventBus - Improvement Plan Overview

## Current State

The project has three main components:
- **Saithis.CloudEventBus** - Core library with interfaces and basic implementations
- **Saithis.CloudEventBus.EfCoreOutbox** - EF Core outbox pattern with interceptor-based approach
- **Saithis.CloudEventBus.RabbitMq** - RabbitMQ sender

The outbox pattern implementation uses an EF Core `SaveChangesInterceptor` to automatically convert staged messages to outbox entities, and a background processor with distributed locking.

## Current goals

### General:

- Exceptional developer experience in combination RabbitMq and EfCore
- Could also work with Kafka, etc.
- Should send messages as CloudEvents by default, but can be disabled globally/per message
- Very good error handling and recovery
- Works with horrizontally scaled applications

### Saithis.CloudEventBus:

- Generic implementation of event/message sending/receiving
- Support for different message serializers with a default one and overwritable per message
- Support for different broker types (RabbitMQ, Kafka, etc.)

### Saithis.CloudEventBus.RabbitMq:

- RabbitMq implementations

### Saithis.CloudEventBus.EfCoreOutbox:

- Implements the Outbox pattern via EfCore 
- Exceptional developer experience
- Low latency
- Low resource usage

## Future goals

### Message consumption

- Support for multiple handlers of the same message
- Support for the inbox pattern
- Idempotent message processing

### Observabillity

- metrics
- traces

### Testing

- Make it easy for users to write tests for sending/receiving events/messages
- InMemoryMessageSender that collects messages for assertions
- FakeOutboxProcessor that processes synchronously for tests
- Test helpers like bus.ShouldHavePublished<T>(predicate)

### Tests

- Test everything properly
- Prefer integration/e2e tests
- Use unit tests only for specific cases where it makes more sense than integration/e2e tests

### RabbitMq auto provision

- Auto provision queues/exchanges
- Dead letter queue configuration

### UI

- See the status of the inbox/outbox
- Requeue of poisoned messages

### AsyncApi support

- Also make it possible to show to which channel incoming messages belong

### Documentation

## Implementation Plans

### Completed Plans

| Plan | Name | Status | Notes |
|------|------|--------|-------|
| 01 | Fix Outbox Error Handling | ✅ Completed | Retry logic, exponential backoff, poison messages |
| 02 | Fix Outbox Reliability | ✅ Completed | Processing state, stuck message recovery, indexes |
| 03 | Implement RabbitMQ Sender | ✅ Completed | Connection management, publisher confirms |
| 04 | Add Message Type Registration | ✅ Completed | [CloudEvent] attribute, MessageTypeRegistry |
| 05 | Implement CloudEvents Format | ✅ Completed | Structured and binary content modes |
| 06 | Add Configuration Options | ✅ Completed | OutboxOptions, builder pattern |
| 07 | Unify Publishing API | ✅ Completed | PublishDirectAsync vs OutboxMessages.Add |
| 08 | Testing Support | ✅ Completed | InMemoryMessageSender, SynchronousOutboxProcessor, test assertions |

### Future Plans

| Plan | Name | Status | Notes |
|------|------|--------|-------|
| 09 | Message Consuming | Planned | IMessageHandler, RabbitMQ consumer, multiple handlers per message |
| 10 | Inbox Pattern | Planned | Idempotent message handling via EF Core |

### Execution Order
1. **Plan 08** - Testing support makes it easier to test Plans 09/10
2. **Plan 09** - Foundation for receiving messages
3. **Plan 10** - Builds on message consuming infrastructure

## Questions

- Should Saithis.CloudEventBus.EfCoreOutbox be renamed to Saithis.CloudEventBus.EfCore for inbox pattern or should it be a separate package?