# TODOs

## Logic

### Consumer failure handling

- Add dead letter queue support.
- Delay messages for retry.
- Make retry logic configurable.

## CloudEvents

- Make sure cloud events are serialized according to spec.
- Prefer clound events headers over message properties.
- Support cloud events extensions.
- Support cloud events extensions in deserialization.
- Allow single messages to opt-in/out of cloud events.
- Detect cloud events wrapper automatically in deserializer.
- Propper tests for cloud events serializer

## DX

### Improve config APIs

Some are fluent, others are not, etc... 

### Improve sending APIs
        Extensions =
        {
            [RabbitMqMessageSender.RoutingKeyExtensionKey] = "notes.test"
        }

This is awkward to use