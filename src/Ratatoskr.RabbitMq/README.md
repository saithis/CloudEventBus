# Ratatoskr.RabbitMq

RabbitMQ transport implementation for the Ratatoskr CloudEventBus.

## Features

- **RabbitMQ Integration**: robust publishing and consuming using RabbitMQ.
- **Topology Management**: Automatic declaration of exchanges, queues, and bindings.
- **Reliability**: Supports retries, dead-letter exchanges, and persistent connections.

## Getting Started

Install the package via NuGet:

```bash
dotnet add package Ratatoskr.RabbitMq
```

Configure `Ratatoskr` to use RabbitMQ:

```csharp
builder.Services.AddRatatoskr(r =>
{
    r.AddRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = "guest";
        options.Password = "guest";
    });
});
```
