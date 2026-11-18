using System.Text.Json.Serialization;

namespace ProjectMass.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExerciseType { Strength, Cardio }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MuscleGroup { Brust, Rücken, Schultern, Bizeps, Trizeps, Beine, Gesäß, Waden, Bauch, Ganzkörper, Cardio }

public sealed class Exercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<MuscleGroup> MuscleGroups { get; set; } = [];
    public string Equipment { get; set; } = string.Empty;
    public ExerciseType Type { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = [];
    public List<string> Tips { get; set; } = [];
    public List<string> Mistakes { get; set; } = [];
    public string? ImagePath { get; set; }
    public string? ImagePath2 { get; set; }
    public string? VideoUrl { get; set; }
}
