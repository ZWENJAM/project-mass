namespace ProjectMass.Models;

public sealed class WorkoutPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DayOfWeek? Day { get; set; }
    public string Note { get; set; } = string.Empty;
    public List<PlannedExercise> Exercises { get; set; } = [];
}

public sealed class PlannedExercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExerciseId { get; set; }
    public int Sets { get; set; } = 3;
    public int MinReps { get; set; } = 8;
    public int MaxReps { get; set; } = 12;
    public int RestSeconds { get; set; } = 120;
}
