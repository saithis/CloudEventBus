using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaygroundApi.Database;
using PlaygroundApi.Database.Entities;
using PlaygroundApi.Events;
using Saithis.CloudEventBus;
using Saithis.CloudEventBus.EfCoreOutbox;

var builder = WebApplication.CreateBuilder(args);

// https://github.com/madelson/DistributedLock
var lockFileDirectory = new DirectoryInfo(Environment.CurrentDirectory); // choose where the lock files will live
builder.Services.AddSingleton<IDistributedLockProvider>(_ => new FileDistributedSynchronizationProvider(lockFileDirectory));

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddMessageBus();
builder.Services.AddOutboxPattern<NotesDbContext>();
builder.Services.AddDbContext<NotesDbContext>((sp, c) => c
    .UseInMemoryDatabase("notesDb")
    .RegisterOutbox<NotesDbContext>(sp));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/notes", async ([FromBody] NoteDto dto, [FromServices] NotesDbContext db) =>
{
    var note = new Note
    {
        Text = dto.Text,
        CreatedAt = DateTime.Now,
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