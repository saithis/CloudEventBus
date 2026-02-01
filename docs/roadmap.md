# Roadmap

1. DX, stability and code health

    - Getting AI generated code into shape

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

1. AsyncApi support

    - Also make it possible to show to which channel incoming messages belong

1. Documentation

1. Release

    - Release the library
    - Create nuget packages

1. UI

    - See the status of the inbox/outbox
    - Requeue of poisoned messages