# CloudEventBus

## Goals

- Exceptional developer experience in combination RabbitMq and EfCore
- Abstractions to make other transports like Kafka possible
- Send messages as CloudEvents
- Very good error handling and recovery
- Works with horrizontally scaled applications
- Good Observability
- Easy to test

## Saithis.CloudEventBus:

- Generic implementation of event/message sending/receiving
- Support for different message serializers with a default one and overwritable per message
- Support for multiple handlers of the same message

## Saithis.CloudEventBus.RabbitMq:

- RabbitMq implementations for sending, receiving and CloudEvents mapping

## Saithis.CloudEventBus.EfCore:

- Implements the Outbox pattern via EfCore 
- Exceptional developer experience
- Low latency
- Low resource usage
