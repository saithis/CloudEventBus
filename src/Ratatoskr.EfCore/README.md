# Ratatoskr.EfCore

Entity Framework Core integration for the Ratatoskr CloudEventBus, enabling the Outbox pattern for reliable event publishing.

## Features

- **Transactional Outbox**: Persist events to the database within the same transaction as your business data.
- **Automatic Processing**: Background worker to publish events from the outbox.

## Getting Started

Install the package via NuGet:

```bash
dotnet add package Ratatoskr.EfCore
```

Configure `Ratatoskr` to use the EF Core outbox:

```csharp
builder.Services.AddRatatoskr(r =>
{
    r.AddOutbox<MyDbContext>();
});
```
