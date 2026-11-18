namespace ProjectMass.Models;

public enum Gender { Unspecified, Female, Male, Diverse }
public enum ActivityLevel { Sedentary, Light, Moderate, Active, VeryActive }
public enum FitnessGoal { Maintain, Build, Lose }

public sealed class UserProfile
{
    public static readonly Guid SingletonId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public Guid Id { get; set; } = SingletonId;
    public string Name { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public int? BirthYear { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? CurrentWeightKg { get; set; }
    public string? UsdaApiKey { get; set; }
    public ActivityLevel ActivityLevel { get; set; }
    public FitnessGoal Goal { get; set; } = FitnessGoal.Maintain;
    public decimal TargetKcal { get; set; }
    public decimal TargetProteinGrams { get; set; }
    public decimal TargetCarbohydrateGrams { get; set; }
    public decimal TargetFatGrams { get; set; }
    public int TrainingDaysPerWeek { get; set; }
    public int TargetWaterMl { get; set; } = 3000;
    public DateTimeOffset? LastBackupAt { get; set; }
    public decimal UpperBodyIncreaseKg { get; set; } = 2.5m;
    public decimal LowerBodyIncreaseKg { get; set; } = 5m;
}
