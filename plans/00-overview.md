# CloudEventBus - Improvement Plan Overview

## Goals

- Exceptional developer experience in combination RabbitMq and EfCore
- Abstractions to make other transports like Kafka possible
- Send messages as CloudEvents
- Very good error handling and recovery
- Works with horrizontally scaled applications
- Good Observability
- Easy to test

### Saithis.CloudEventBus:

- Generic implementation of event/message sending/receiving
- Support for different message serializers with a default one and overwritable per message
- Support for multiple handlers of the same message

### Saithis.CloudEventBus.RabbitMq:

- RabbitMq implementations for sending, receiving and CloudEvents mapping

### Saithis.CloudEventBus.EfCore:

- Implements the Outbox pattern via EfCore 
- Exceptional developer experience
- Low latency
- Low resource usage

## Roadmap

1. DX, stability and code health

    - Getting AI generated code into shape
    - Improve DX, especially around config
    - Message consumption: Improved error handling, retry logic, etc.

1. Observabillity

    - metrics
    - traces

1. Testing

    - Make it easy for users to write tests for sending/receiving events/messages
    - InMemoryMessageSender that collects messages for assertions
    - FakeOutboxProcessor that processes synchronously for tests
    - Test helpers like bus.ShouldHavePublished<T>(predicate)

1. Tests

    - Test everything properly
    - Prefer integration/e2e tests
    - Use unit tests only for specific cases where it makes more sense than integration/e2e tests

1. Inbox pattern

    - Support for the inbox pattern
    - Idempotent message processing

1. RabbitMq auto provision

    - Auto provision queues/exchanges
    - Dead letter queue configuration

1. UI

    - See the status of the inbox/outbox
    - Requeue of poisoned messages

1. AsyncApi support

    - Also make it possible to show to which channel incoming messages belong

1. Documentation

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
| 09 | Message Consuming | ✅ Completed | IMessageHandler, RabbitMQ consumer, multiple handlers per message |

### Future Plans

| Plan | Name | Status | Notes |
|------|------|--------|-------|
| 10 | Inbox Pattern | Planned | Idempotent message handling via EF Core |


