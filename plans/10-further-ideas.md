# Plan 10: Refine further ideas

Goals:

* General:
    - Exceptional developer experience in combination RabbitMq and EfCore
    - Could also work with Kafka, etc.
    - Should send messages as CloudEvents by default, but can be disabled globally/per message
    - Very good error handling and recovery
    - Works with horrizontally scaled applications

* Saithis.CloudEventBus:
    - Generic implementation of event/message sending
    - Support for different message serializers with a default one and overwritable per message
    - Support for different broker types that actually send the message

* Saithis.CloudEventBus.RabbitMq:
    - RabbitMq implementation for message sending

* Saithis.CloudEventBus.EfCoreOutbox:
    - Implements the Outbox pattern via EfCore 
    - Exceptional developer experience
    - Low latency
    - Low resource usage

Future work:

* Message consumption
    - Support for multiple handlers of the same message
    - Support for the inbox pattern

* Testing
    - Make it easy for users to write tests for sending/receiving events/messages
    - InMemoryMessageSender that collects messages for assertions
    - FakeOutboxProcessor that processes synchronously for tests
    - Test helpers like bus.ShouldHavePublished<T>(predicate)

* UI
    - See the status of the inbox/outbox
    - Requeue of poisoned messages

* AsyncApi support
    - Also make it possible to show to which channel incoming messages belong

* RabbitMq auto provision
    - Auto provision queues/exchanges
    - Dead letter queue configuration

* Observabillity
    - metrics
    - traces

* Documentation

