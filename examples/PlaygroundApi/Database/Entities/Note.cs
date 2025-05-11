namespace PlaygroundApi.Database.Entities;

public class Note
{
    public int Id { get; init; }
    public required string Text { get; set; }
    public required DateTime CreatedAt { get; init; }
}