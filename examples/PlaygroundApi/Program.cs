using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaygroundApi.Database;
using PlaygroundApi.Database.Entities;
using PlaygroundApi.Events;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCore;
using Saithis.CloudEventBus.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults for Aspire
builder.AddServiceDefaults();

// https://github.com/madelson/DistributedLock
var lockFileDirectory = new DirectoryInfo(Environment.CurrentDirectory); // choose where the lock files will live
builder.Services.AddSingleton<IDistributedLockProvider>(_ => new FileDistributedSynchronizationProvider(lockFileDirectory));

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddCloudEventBus(bus => bus
    .AddMessagesFromAssemblyContaining<NoteAddedEvent>());

// Use RabbitMQ when connection string is available (Aspire), otherwise use Console for testing
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("rabbitmq");
if (!string.IsNullOrEmpty(rabbitMqConnectionString))
{
    builder.Services.AddRabbitMqMessageSender(options =>
    {
        options.ConnectionString = rabbitMqConnectionString;
        options.DefaultExchange = "events.topic"; // Use topic exchange for event routing
    });
}
else
{
    builder.Services.AddConsoleMessageSender(); // For testing/development
}

builder.Services.AddOutboxPattern<NotesDbContext>();

// Use PostgreSQL when connection string is available (Aspire), otherwise use InMemory
var connectionString = builder.Configuration.GetConnectionString("notesdb");
if (!string.IsNullOrEmpty(connectionString))
{
    // Use AddDbContext directly to get access to IServiceProvider for interceptor registration
    // This bypasses Aspire's AddNpgsqlDbContext but still uses the connection string from Aspire
    builder.Services.AddDbContext<NotesDbContext>((sp, options) =>
    {
        options.UseNpgsql(connectionString);
        options.RegisterOutbox<NotesDbContext>(sp);
    });
}
else
{
    builder.Services.AddDbContext<NotesDbContext>((sp, c) => c
        .UseInMemoryDatabase("notesDb")
        .RegisterOutbox<NotesDbContext>(sp));
}

var app = builder.Build();

// Map default endpoints for Aspire
app.MapDefaultEndpoints();

// Ensure RabbitMQ topic exchange exists if using RabbitMQ
if (!string.IsNullOrEmpty(rabbitMqConnectionString))
{
    await EnsureRabbitMqExchangeAsync(rabbitMqConnectionString);
}

// Apply migrations if using PostgreSQL
var dbConnectionString = app.Configuration.GetConnectionString("notesdb");
if (!string.IsNullOrEmpty(dbConnectionString))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapGet("/", () => "Hello World!");

// Direct publishing example - no transaction
app.MapPost("/send", async ([FromBody] NoteDto dto, [FromServices] ICloudEventBus bus) =>
{
    // PublishDirectAsync - sends immediately, no outbox
    await bus.PublishDirectAsync(dto, new MessageProperties
    {
        Type = "com.example.notes.test",
        Source = "/playground-api",
        TransportMetadata =
        {
            [RabbitMqMessageSender.RoutingKeyExtensionKey] = "notes.test"
        }
    });
    return TypedResults.Ok(dto);
});

// Outbox publishing example - transactional
app.MapPost("/notes", async ([FromBody] NoteDto dto, [FromServices] NotesDbContext db) =>
{
    var note = new Note
    {
        Text = dto.Text,
        CreatedAt = DateTime.UtcNow,
    };
    
    // Both operations are transactional
    db.Notes.Add(note);
    db.OutboxMessages.Add(new NoteAddedEvent
    {
        Id = note.Id,
        Text = $"New Note: {dto.Text}",
    }, new MessageProperties
    {
        TransportMetadata =
        {
            [RabbitMqMessageSender.RoutingKeyExtensionKey] = "notes.added"
        }
    });
    
    await db.SaveChangesAsync(); // Note saved AND event queued atomically
    return TypedResults.Ok(note);
});
app.MapGet("/notes", async ([FromServices] NotesDbContext db) =>
{
    var notes = await db.Notes.ToListAsync();
    return TypedResults.Ok(notes);
});

app.Run();

static async Task EnsureRabbitMqExchangeAsync(string connectionString)
{
    using var connection = await new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(connectionString) }
        .CreateConnectionAsync();
    using var channel = await connection.CreateChannelAsync();
    
    // Declare a durable topic exchange for events
    await channel.ExchangeDeclareAsync(
        exchange: "events.topic",
        type: RabbitMQ.Client.ExchangeType.Topic,
        durable: true,
        autoDelete: false);
}

public record NoteDto(string Text);