# CloudEventBus

A .NET library for implementing cloud event patterns with support for outbox pattern and multiple message brokers.

## Getting Started

### Quick Start with .NET Aspire

The easiest way to run the example application is using the .NET Aspire AppHost.

First, install the Aspire workload if you haven't already:

```bash
dotnet workload install aspire
```

Then run the AppHost:

```bash
cd examples/AppHost
dotnet run
```

This will start:
- PostgreSQL database
- RabbitMQ message broker
- PlaygroundApi example application
- Aspire Dashboard at http://localhost:15000

See [examples/AppHost/README.md](examples/AppHost/README.md) for more details.

## Project Structure

- `src/Saithis.CloudEventBus` - Core library
- `src/Saithis.CloudEventBus.EfCoreOutbox` - Entity Framework Core outbox pattern implementation
- `src/Saithis.CloudEventBus.RabbitMq` - RabbitMQ message sender
- `examples/PlaygroundApi` - Example API application
- `examples/AppHost` - .NET Aspire orchestration