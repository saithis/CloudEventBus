# Source Directory Overview

This `src` directory contains the core implementation of the Ratatoskr library.

## Project Structure

- **Ratatoskr**: The core, transport-agnostic library. It defines the interfaces, models (CloudEvents), and base logic. This project should *not* depend on specific external transports (like RabbitMQ) or persistence references (like EF Core) directly, keeping the core clean.
- **Ratatoskr.RabbitMq**: The RabbitMQ implementation of the transport. Depends on `Ratatoskr`.
- **Ratatoskr.EfCore**: The Entity Framework Core implementation for the Outbox pattern. Depends on `Ratatoskr`.

## Coding Standards

- **Nullable Reference Types**: Enabled globally. Ensure null checks are appropriate.
- **Async/Await**: Use asynchronous patterns for all I/O bound operations.
- **Dependency Injection**: All services should be designed for `Microsoft.Extensions.DependencyInjection`.
- **Logging**: Use partial methods for high-performance logging (`LoggerMessage` attribute) where possible.
