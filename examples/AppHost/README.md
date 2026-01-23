# CloudEventBus AppHost

This is a .NET Aspire AppHost that orchestrates the PlaygroundApi along with its dependencies:

- **PostgreSQL** - Database for storing notes
- **RabbitMQ** - Message broker for the event bus
- **PlaygroundApi** - The example API application

## Prerequisites

- .NET 10 SDK
- Docker (for running PostgreSQL and RabbitMQ containers)
- .NET Aspire workload (install with: `dotnet workload install aspire`)

### Installing Aspire

If you encounter an error about missing DCP or Dashboard paths, install the Aspire workload:

```bash
dotnet workload install aspire
```

## Running the AppHost

To start all services:

```bash
cd examples/AppHost
dotnet run
```

This will:
1. Start PostgreSQL container with pgAdmin
2. Start RabbitMQ container with management plugin
3. Start the PlaygroundApi with connection to both services
4. Open the Aspire Dashboard at http://localhost:15000

## Accessing Services

### Aspire Dashboard
- **URL**: http://localhost:15000
- View logs, traces, and metrics for all services

### PlaygroundApi
- The API will be available on a random port assigned by Aspire
- Check the Aspire Dashboard for the exact URL

### RabbitMQ Management
- Access via the Aspire Dashboard or check the RabbitMQ resource for credentials

### pgAdmin
- Access via the Aspire Dashboard to manage PostgreSQL

## Features

- **Automatic Service Discovery**: All services are automatically configured to find each other
- **Observability**: Built-in OpenTelemetry support for logs, traces, and metrics
- **Health Checks**: Automatic health monitoring for all services
- **Development Tools**: pgAdmin and RabbitMQ Management UI included

## Configuration

The AppHost automatically configures:
- Database connection strings
- RabbitMQ connection settings
- Service-to-service communication
- OpenTelemetry endpoints

No manual configuration is required!
