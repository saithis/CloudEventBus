# Core Library (Saithis.CloudEventBus)

This project contains the fundamental abstractions and core logic for the CloudEventBus.

## Key Abstractions

- **`ICloudEventBus`**: The primary interface for publishing events. Developers use this intreface to send messages.
- **`MessageProperties`**: Holds additional message properties like headers, content type, etc. It is used by the transports to send the message.
- **`CloudEventEnvelope`**: The generic wrapper for messages that use the structured mode. It encapsulates the payload and standard CloudEvent attributes (Id, Source, Type, etc.). For binary mode (default) the payload is the message itself and the CloudEvents attributes are set as headers.
- **`IMessageHandler<T>`**: (Pattern) The library supports multiple handlers for the same message type.

## Extension Points

- **`CloudEventBusBuilder`**: Used to configure the bus, register transports, and add middlewares.
- **Serialization**: The library supports pluggable serializers. The default serializer should be sufficient for most JSON-based CloudEvents.

## Development Guidelines

- **Zero Dependencies**: This core project *must not* depend on RabbitMQ, Kafka, or EF Core. It should remain pure.
- **Attributes**: Use attributes like `[CloudEvent]` (if available/planned) to decorate message classes for auto-discovery or metadata.
- **Extensible**: Provide extension points for custom implementations of transports, serializers, and middlewares. But provide as much of the logic in a generic way as possible.
