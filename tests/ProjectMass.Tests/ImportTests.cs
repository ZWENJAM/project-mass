using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class ImportTests
{
    private readonly InMemoryStorageService storage = new();
    private PlanImportService Plans => new(storage);
    private NutritionImportService Nutrition => new(storage, new RecipeService(storage, new NutritionCalculator()), new FixedClock());

    // Typical AI answer: a sentence, a fenced code block and a trailing comma.
    private const string PlanAnswer = """
        Hier ist dein Plan:
        ```json
        {
          "format": "project-mass-plan",
          "version": 1,
          "plans": [
            { "name": "Push", "day": "Montag", "note": "Brust, Schultern",
              "exercises": [
                { "name": "bankdrücken", "sets": 4, "reps": "6-10", "restSeconds": 150, "muscleGroups": ["Brust", "Trizeps"] },
                { "name": "Kabel-Crossover", "sets": 3, "reps": 12, "muscleGroups": ["brust", "Unsinn"], "equipment": "Kabelzug" },
                { "name": "", "sets": 3 },
              ] },
            { "name": "Pull", "day": "Freitag", "exercises": [ { "name": "Latziehen", "sets": 99, "reps": "8-12", "restSeconds": 5 } ] }
          ]
        }
        ```
        Viel Erfolg!
        """;

    [Fact]
    public async Task PlanAnswerWithFencesIsParsedAndValuesAreClamped()
    {
        await storage.PutAsync(StorageStores.Exercises, new Exercise { Name = "Bankdrücken" });
        var preview = await Plans.PreviewAsync(PlanAnswer);

        Assert.Equal(2, preview.Plans.Count);
        var push = preview.Plans[0];
        Assert.Equal(DayOfWeek.Monday, push.Day);
        Assert.Equal(2, push.Exercises.Count);
        Assert.Single(preview.Warnings);
        Assert.False(push.Exercises[0].IsNew);            // matched case-insensitively
        Assert.Equal((4, 6, 10, 150), (push.Exercises[0].Sets, push.Exercises[0].MinReps, push.Exercises[0].MaxReps, push.Exercises[0].RestSeconds));
        Assert.True(push.Exercises[1].IsNew);
        Assert.Equal((12, 12), (push.Exercises[1].MinReps, push.Exercises[1].MaxReps));
        Assert.Equal(new[] { MuscleGroup.Brust }, push.Exercises[1].MuscleGroups.ToArray());
        var pull = preview.Plans[1].Exercises[0];
        Assert.Equal((10, 30), (pull.Sets, pull.RestSeconds));
    }

    [Fact]
    public async Task ImportCreatesPlansAndOnlyMissingExercises()
    {
        await storage.PutAsync(StorageStores.Exercises, new Exercise { Name = "Bankdrücken" });
        await storage.PutAsync(StorageStores.WorkoutPlans, new WorkoutPlan { Name = "Push" });
        Assert.Equal(2, await Plans.ImportAsync(await Plans.PreviewAsync(PlanAnswer)));

        var exercises = await storage.GetAllAsync<Exercise>(StorageStores.Exercises);
        Assert.Equal(3, exercises.Count); // Bankdrücken existed, Kabel-Crossover and Latziehen are new
        var plans = await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans);
        Assert.Contains(plans, p => p.Name == "Push (Import)" && p.Exercises.Count == 2);
        var bench = exercises.Single(e => e.Name == "Bankdrücken");
        Assert.Equal(bench.Id, plans.Single(p => p.Name == "Push (Import)").Exercises[0].ExerciseId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Leider kann ich das nicht.")]
    [InlineData("{ \"plans\": [ { \"name\": \"Leer\", \"exercises\": [] } ] }")]
    [InlineData("{ \"plans\": [ }")]
    public async Task UnusablePlanAnswersGiveReadableErrors(string text) =>
        await Assert.ThrowsAsync<ImportFormatException>(() => Plans.PreviewAsync(text));

    [Fact]
    public async Task PlanImportAddsGuideFieldsAndPreservesExistingContent()
    {
        await storage.PutAsync(StorageStores.Exercises, new Exercise
        {
            Name = "Bankdrücken", Steps = ["Eigener Schritt"], VideoUrl = "https://example.org/eigen"
        });
        var preview = await Plans.PreviewAsync("""
            { "name": "Training", "exercises": [
              { "name": "Bankdrücken", "steps": ["Ignorieren"], "tips": ["  Stabil greifen  ", "", 5],
                "mistakes": ["Schwung holen"], "videoUrl": "https://example.org/import" },
              { "name": "Rudern", "steps": ["Aufrecht sitzen"], "tips": ["Rumpf ruhig halten"],
                "mistakes": ["Zurücklehnen"], "videoUrl": "https://example.org/video" },
              { "name": "Klimmzug", "videoUrl": "http://example.org/video" }
            ]}
            """);
        Assert.Contains("HTTPS", Assert.Single(preview.Warnings));
        await Plans.ImportAsync(preview);

        var catalog = await new ExerciseService(storage).GetAllAsync();
        var bench = catalog.Single(e => e.Name == "Bankdrücken");
        Assert.Equal(["Eigener Schritt"], bench.Steps);
        Assert.Equal(["Stabil greifen"], bench.Tips);
        Assert.Equal(["Schwung holen"], bench.Mistakes);
        Assert.Equal("https://example.org/eigen", bench.VideoUrl);
        var row = catalog.Single(e => e.Name == "Rudern");
        Assert.Equal(["Aufrecht sitzen"], row.Steps);
        Assert.Equal(["Rumpf ruhig halten"], row.Tips);
        Assert.Equal(["Zurücklehnen"], row.Mistakes);
        Assert.Equal("https://example.org/video", row.VideoUrl);
        Assert.Null(catalog.Single(e => e.Name == "Klimmzug").VideoUrl);
    }

    private const string NutritionAnswer = """
        {
          "format": "project-mass-nutrition", "version": 1,
          "targets": { "kcal": 2600, "protein": 180, "carbs": 300, "fat": 80, "waterMl": 3000 },
          "foods": [
            { "name": "Skyr natur", "category": "Dairy", "per100g": { "kcal": 63, "protein": 11, "carbs": 4, "fat": 0.2 }, "portions": [ { "name": "1 Becher", "grams": 450 } ] },
            { "name": "Heidelbeeren", "per100g": { "kcal": 57, "protein": 0.7, "carbs": 12, "fat": 0.3 } },
            { "name": "Kaputt", "per100g": { "kcal": 5000 } }
          ],
          "recipes": [
            { "name": "Skyr-Bowl", "portions": 1, "ingredients": [ { "food": "Skyr natur", "grams": 300 }, { "food": "heidelbeeren", "grams": 100 }, { "food": "Gibt es nicht", "grams": 50 } ] }
          ],
          "tips": [ "Protein auf 4 Mahlzeiten verteilen." ]
        }
        """;

    [Fact]
    public async Task NutritionPreviewValidatesFoodsAndResolvesIngredients()
    {
        var preview = await Nutrition.PreviewAsync(NutritionAnswer);
        Assert.Equal(2, preview.Foods.Count);
        Assert.Equal(FoodCategory.Dairy, preview.Foods[0].Category);
        Assert.Equal(450m, preview.Foods[0].PortionSizes[0].Grams);
        Assert.Equal(2, Assert.Single(preview.Recipes).Ingredients.Count);
        Assert.Equal(2, preview.Warnings.Count); // implausible kcal + unknown ingredient
        Assert.Equal(2600m, preview.Targets!.Kcal);
    }

    [Fact]
    public async Task NutritionImportKeepsExistingFoodsAndAppliesTargetsOnlyWhenAsked()
    {
        await storage.PutAsync(StorageStores.FoodItems, new FoodItem { Name = "Skyr natur", NutrientsPer100G = new() { Kcal = 65 } });
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { TargetKcal = 2200 });

        var result = await Nutrition.ImportAsync(await Nutrition.PreviewAsync(NutritionAnswer), applyTargets: false);
        Assert.Equal((1, 1, 1, false, true), (result.Foods, result.SkippedFoods, result.Recipes, result.TargetsApplied, result.TipsSaved));
        Assert.Equal(65m, (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems)).Single(f => f.Name == "Skyr natur").NutrientsPer100G.Kcal);
        Assert.Equal(2200m, (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!.TargetKcal);
        Assert.Contains("Protein auf 4 Mahlzeiten", Assert.Single(await storage.GetAllAsync<Note>(StorageStores.Notes)).Text);

        await Nutrition.ImportAsync(await Nutrition.PreviewAsync(NutritionAnswer), applyTargets: true);
        var profile = (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!;
        Assert.Equal((2600m, 180m, 3000), (profile.TargetKcal, profile.TargetProteinGrams, profile.TargetWaterMl));
    }

    [Fact]
    public void PromptsContainProfileAndFormat()
    {
        var profile = new UserProfile { Goal = FitnessGoal.Build, TrainingDaysPerWeek = 4 };
        var planPrompt = Plans.BuildPrompt(profile, 92.4m, [new Exercise { Name = "Latziehen" }]);
        Assert.Contains("Muskelaufbau", planPrompt);
        Assert.Contains("92,4 kg", planPrompt);
        Assert.Contains("Latziehen", planPrompt);
        Assert.Contains("\"format\": \"project-mass-plan\"", planPrompt);
        Assert.Contains("\"steps\"", planPrompt);
        Assert.Contains("\"tips\"", planPrompt);
        Assert.Contains("\"mistakes\"", planPrompt);
        Assert.Contains("\"videoUrl\"", planPrompt);
        Assert.Contains("\"format\": \"project-mass-nutrition\"", Nutrition.BuildPrompt(profile, 92.4m));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    }
}
