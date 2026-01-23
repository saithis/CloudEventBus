using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaygroundApi.Database;
using PlaygroundApi.Database.Entities;
using PlaygroundApi.Events;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.Core;
using Saithis.CloudEventBus.EfCoreOutbox;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults for Aspire
builder.AddServiceDefaults();

// https://github.com/madelson/DistributedLock
var lockFileDirectory = new DirectoryInfo(Environment.CurrentDirectory); // choose where the lock files will live
builder.Services.AddSingleton<IDistributedLockProvider>(_ => new FileDistributedSynchronizationProvider(lockFileDirectory));

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddMessageBus();
builder.Services.AddOutboxPattern<NotesDbContext>();

// Use PostgreSQL when connection string is available (Aspire), otherwise use InMemory
var connectionString = builder.Configuration.GetConnectionString("notesdb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.AddNpgsqlDbContext<NotesDbContext>("notesdb");
    
    // Configure DbContext options to register the outbox
    builder.Services.AddOptions<DbContextOptions<NotesDbContext>>()
        .Configure<IServiceProvider>((options, sp) =>
        {
            var dbOptions = new DbContextOptionsBuilder<NotesDbContext>(options);
            dbOptions.RegisterOutbox<NotesDbContext>(sp);
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

// Apply migrations if using PostgreSQL
var dbConnectionString = app.Configuration.GetConnectionString("notesdb");
if (!string.IsNullOrEmpty(dbConnectionString))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

app.MapGet("/", () => "Hello World!");

app.MapPost("/send", async ([FromBody] NoteDto dto, [FromServices] ICloudEventBus bus) =>
{
    await bus.PublishAsync(dto, new MessageProperties());
    return TypedResults.Ok(dto);
});

app.MapPost("/notes", async ([FromBody] NoteDto dto, [FromServices] NotesDbContext db) =>
{
    var note = new Note
    {
        Text = dto.Text,
        CreatedAt = DateTime.UtcNow,
    };
    db.Notes.Add(note);
    db.OutboxMessages.Add(new NoteAddedEvent
    {
        Id = note.Id,
        Text = $"New Note: {dto.Text}",
    });
    await db.SaveChangesAsync();
    return TypedResults.Ok(note);
});
app.MapGet("/notes", async ([FromServices] NotesDbContext db) =>
{
    var notes = await db.Notes.ToListAsync();
    return TypedResults.Ok(notes);
});

app.Run();


public record NoteDto(string Text);