# TODOs

## Logic

### Consumer failure handling

- Add dead letter queue support.
- Delay messages for retry.
- Make retry logic configurable.

## CloudEvents

- Allow single messages to opt-in/out of cloud events.

## DX

### Improve config APIs

Some are fluent, others are not, etc... 

### Improve sending APIs
        Extensions =
        {
            [RabbitMqMessageSender.RoutingKeyExtensionKey] = "notes.test"
        }

This is awkward to use