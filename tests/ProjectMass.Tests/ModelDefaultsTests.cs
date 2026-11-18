using System.Text.Json;
using ProjectMass.Models;

namespace ProjectMass.Tests;

public class ModelDefaultsTests
{
    [Fact]
    public void NewSetsAreIndependentUncompletedWorkingSets()
    {
        var first = new SetEntry();
        var second = new SetEntry();
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.IsCompleted);
        Assert.Equal(SetType.Working, first.Type);
        Assert.Null(first.Rpe);
    }

    [Fact]
    public void WorkoutPlansDoNotShareTheirExerciseLists()
    {
        var first = new WorkoutPlan();
        var second = new WorkoutPlan();
        first.Exercises.Add(new PlannedExercise());
        Assert.Empty(second.Exercises);
        Assert.Equal(120, first.Exercises[0].RestSeconds);
    }

    [Fact]
    public void FoodLogSnapshotAndDateSurviveInteropSerialization()
    {
        var entry = new FoodLogEntry
        {
            Date = new DateOnly(2026, 11, 16),
            Meal = MealType.Lunch,
            FoodSnapshot = new FoodItem
            {
                Name = "Haferflocken",
                NutrientsPer100G = new Nutrients { Kcal = 370, ProteinGrams = 13 }
            },
            AmountGrams = 75
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(entry, options);
        var restored = JsonSerializer.Deserialize<FoodLogEntry>(json, options)!;
        Assert.Contains("\"id\":", json);
        Assert.Equal(entry.Id, restored.Id);
        Assert.Equal(entry.Date, restored.Date);
        Assert.Equal(entry.FoodSnapshot.NutrientsPer100G, restored.FoodSnapshot.NutrientsPer100G);
        Assert.Equal(75m, restored.AmountGrams);
    }
}
