namespace PlaygroundApi.Events;

public class NoteAddedEvent
{
    public required int Id { get; init; }
    public required string Text { get; init; }
}