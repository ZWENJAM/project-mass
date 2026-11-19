namespace ProjectMass.Models;

public enum SetType { Working, WarmUp, Drop }

public sealed class WorkoutSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WorkoutPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Note { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? RestEndsAt { get; set; }
    public List<SessionExercise> Exercises { get; set; } = [];
    public List<PersonalRecord> NewPersonalRecords { get; set; } = [];
}

public sealed class SessionExercise
{
    public bool ProgressionApplied { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExerciseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MinReps { get; set; } = 8;
    public int MaxReps { get; set; } = 12;
    public int RestSeconds { get; set; } = 120;
    public List<SetEntry> Sets { get; set; } = [];
}

public sealed class SetEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal WeightKg { get; set; }
    public int Reps { get; set; }
    public decimal? Rpe { get; set; }
    public bool IsCompleted { get; set; }
    public SetType Type { get; set; } = SetType.Working;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PrefilledFromHistory { get; set; }
}
