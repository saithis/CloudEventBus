using Saithis.CloudEventBus;

namespace PlaygroundApi.Events;

[CloudEvent("com.example.notes.added")]
public class NoteAddedEvent
{
    public required int Id { get; init; }
    public required string Text { get; init; }
}