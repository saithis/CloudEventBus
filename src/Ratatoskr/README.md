# Ratatoskr

Ratatoskr is a lightweight CloudEvents bus implementation for .NET, designed to simplify event-driven architecture.

## Features

- **CloudEvents Support**: Native support for the CloudEvents specification.
- **Pluggable Transports**: Easily switch between different messaging transports (e.g., RabbitMQ, In-Memory).
- **Middleware Pipeline**: robust middleware pipeline for cross-cutting concerns.
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection.

## Getting Started

Install the package via NuGet:

```bash
dotnet add package Ratatoskr
```

Configure the services in your `Startup.cs` or `Program.cs`:

```csharp
builder.Services.AddRatatoskr(r =>
{
    // Configure transports and options
});
```
