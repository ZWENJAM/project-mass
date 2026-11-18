namespace ProjectMass.Models;

public enum RecordKind { EstimatedOneRepMax, Weight, Reps }
public sealed record PersonalRecord(Guid ExerciseId, string ExerciseName, RecordKind Kind, decimal Value, DateOnly Date, Guid SessionId);
public sealed record ChartPoint(DateOnly Date, decimal Value);
public sealed record MuscleVolume(MuscleGroup MuscleGroup, decimal VolumeKg, int WorkingSets);
public sealed record FrequencyDay(DateOnly Date, int Sessions);
public sealed record WeeklyReport(DateOnly WeekStart, int LoggedDays, decimal? AverageKcal, decimal? AverageProtein,
    decimal? AverageWeight, decimal? PreviousAverageWeight, decimal? WeightChange, int Trainings, decimal TotalVolumeKg);
public sealed record ProgressionSuggestion(Guid ExerciseId, decimal IncreaseKg);
