# EF Core Outbox (Ratatoskr.EfCore)

This project implements the transactional Outbox pattern using Entity Framework Core.

## The Outbox Pattern

Instead of sending messages directly to the broker (which could fail after a DB commit, leading to inconsistency), messages are saved to an `Outbox` table in the same transaction as the business data. A separate background process (or immediately after commit) picks them up and sends them.

## Key Components

- **`IOutboxDbContext`**: Interfaces that your application's `DbContext` should implement (or expose the required `DbSet`).
- **`OutboxStagingCollection`**: Holds messages temporarily before they are flushed to the `DbContext`.
- **`OutboxOptions`**: Configuration for the outbox behavior (polling intervals, cleanup, etc.).

## Usage

- Developers add this via `AddRatatoskr(..., builder => builder.AddEfCoreOutbox(...))`.
- It ensures **At-Least-Once** delivery.

## Database Considerations

- The library expects an `Outbox` table.
- Migrations might be needed when updating this library if the schema changes.
