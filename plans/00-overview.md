# CloudEventBus - Improvement Plan Overview

## Current State

The project has three main components:
- **Saithis.CloudEventBus** - Core library with interfaces and basic implementations
- **Saithis.CloudEventBus.EfCoreOutbox** - EF Core outbox pattern with interceptor-based approach
- **Saithis.CloudEventBus.RabbitMq** - RabbitMQ sender (placeholder only)

The outbox pattern implementation uses an EF Core `SaveChangesInterceptor` to automatically convert staged messages to outbox entities, and a background processor with distributed locking.

## Goals

- Works well with RabbitMq, but could theoretically also work with Kafka, Azure ServiceBus, etc.
- Implements the Outbox pattern
- Should in the future also support the inbox pattern
- Has very good integration with EfCore
- Should send messages as CloudEvents, but could also work without it
- Should make it easy for users to write tests for sending/receiving events
- Performance is good
- Latency for sending messages is very low
- Has good error handling and recovery
- Should in the future have a nice UI to see the status of the inbox/outbox and recover messages

---

## Identified Problems

### Critical Bugs

1. **Outbox error handling is broken** - Messages that fail to send retry forever with no backoff or dead-letter handling
2. **Serialization failure causes duplicates** - If serialization fails mid-loop, successfully processed items can be duplicated on retry
3. **Race condition risk** - Potential for duplicate message sends if distributed lock times out

### Missing Core Functionality

4. **RabbitMQ sender not implemented** - Only a placeholder exists
5. **No message type identification** - No way to identify message types for routing/deserialization
6. **No CloudEvents format** - Despite the name, no CloudEvents envelope is generated
7. **No message consuming support** - Only publishing is implemented

### Usability Issues

8. **Two conflicting publishing APIs** - Direct via `ICloudEventBus` and via `DbContext.OutboxMessages`
9. **No configuration options** - Hard-coded values for batch size, delays, retries
10. **No database indexes** - Full table scans as outbox grows

### Missing Advanced Features

11. **No inbox pattern** - Needed for idempotent message processing
12. **No observability** - No correlation IDs, OpenTelemetry, or metrics

---

## Improvement Plan (Ordered by Priority)

| # | Plan File | Description |
|---|-----------|-------------|
| 1 | `01-fix-outbox-error-handling.md` | Fix retry logic, add max retries, exponential backoff, dead-letter state |
| 2 | `02-fix-outbox-reliability.md` | Fix serialization loop issue, add processing state, add database indexes |
| 3 | `03-implement-rabbitmq-sender.md` | Implement the actual RabbitMQ message sender |
| 4 | `04-add-message-type-registration.md` | Add message type registration via attributes and fluent API |
| 5 | `05-implement-cloudevents-format.md` | Implement proper CloudEvents envelope format |
| 6 | `06-add-configuration-options.md` | Add builder pattern and options classes for configuration |
| 7 | `07-unify-publishing-api.md` | Clarify the two publishing paths (Option B: explicit separation) |
| 8 | `08-add-message-consuming.md` | Add message handler interface and consumer infrastructure |
| 9 | `09-implement-inbox-pattern.md` | Add inbox pattern for idempotent message processing |

---

## Design Decisions

### Publishing API (Plan 07)
**Decision: Option B - Explicit Separation**
- `ICloudEventBus.PublishAsync()` for immediate publishing (no transactional guarantee)
- `DbContext.OutboxMessages.Add()` for outbox-based publishing (transactional)

Both approaches have valid use cases. Direct publishing is useful for fire-and-forget scenarios where you don't need transactional guarantees.

---

## Dependencies Between Plans

```
Plan 01 (Error Handling) ──┐
                           ├──> Plan 03 (RabbitMQ) ──> Plan 04 (Message Types) ──> Plan 05 (CloudEvents)
Plan 02 (Reliability) ─────┘                                    │
                                                                v
Plan 06 (Configuration) ────────────────────────────────> Plan 07 (Unify API)
                                                                │
                                                                v
                                                    Plan 08 (Consuming) ──> Plan 09 (Inbox)
```

Plans 01-02 fix critical bugs and should be done first.
Plans 03-05 build the core message pipeline.
Plans 06-07 improve usability.
Plans 08-09 add consuming capabilities.
