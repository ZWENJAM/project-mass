namespace ProjectMass.Models;

public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Text { get; set; } = string.Empty;
}
