using System.Net;
using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class TrainingDataTests
{
    [Fact]
    public async Task SeedsSixtyExercisesAndFourLinkedOriginalPlansOnlyOnce()
    {
        var storage = new InMemoryStorageService();
        using var http = new HttpClient(new SeedFilesHandler()) { BaseAddress = new Uri("http://localhost/") };
        await new TrainingDataInitializer(http, storage).InitializeAsync();
        var exercises = await new ExerciseService(storage).GetAllAsync();
        var plans = await new WorkoutPlanService(storage).GetAllAsync();
        Assert.Equal(60, exercises.Count);
        Assert.Equal(6, (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems)).Count);
        Assert.Equal(60, exercises.Select(e => e.Id).Distinct().Count());
        Assert.All(exercises, e => { Assert.NotEmpty(e.MuscleGroups); Assert.NotEmpty(e.Equipment); });
        Assert.All(exercises, e =>
        {
            Assert.InRange(e.Steps.Count, 3, 6);
            Assert.InRange(e.Tips.Count, 2, 4);
            Assert.InRange(e.Mistakes.Count, 2, 3);
        });
        Assert.Equal(Enum.GetValues<MuscleGroup>().Order(), exercises.SelectMany(e => e.MuscleGroups).Distinct().Order());
        Assert.Equal(new[] { "Oberkörper A", "Unterkörper A", "Oberkörper B", "Unterkörper B" }, plans.Select(p => p.Name));
        Assert.Equal(new[] { 7, 5, 7, 5 }, plans.Select(p => p.Exercises.Count));
        Assert.Equal(new[] { 20, 15, 19, 15 }, plans.Select(p => p.Exercises.Sum(e => e.Sets)));
        Assert.All(plans.SelectMany(p => p.Exercises), e => Assert.Contains(exercises, item => item.Id == e.ExerciseId));
        Assert.Equal("Brustpresse", exercises.Single(e => e.Id == plans[0].Exercises[0].ExerciseId).Name);
        Assert.Equal(20, plans[1].Exercises[3].MaxReps);
        Assert.Equal(2, plans[2].Exercises[3].Sets);

        // A new app instance must not recreate deleted plans or overwrite edited records.
        await storage.DeleteAsync(StorageStores.WorkoutPlans, plans[0].Id);
        exercises[0].Note = "Eigene Notiz";
        await storage.PutAsync(StorageStores.Exercises, exercises[0]);
        await new TrainingDataInitializer(http, storage).InitializeAsync();
        Assert.Equal(3, (await new WorkoutPlanService(storage).GetAllAsync()).Count);
        Assert.Equal("Eigene Notiz", (await storage.GetAsync<Exercise>(StorageStores.Exercises, exercises[0].Id))!.Note);
    }

    [Fact]
    public async Task StartupDoesNotMigrateVersionOneExerciseOrSeedMissingStores()
    {
        var storage = new InMemoryStorageService();
        using var http = new HttpClient(new SeedFilesHandler()) { BaseAddress = new Uri("http://localhost/") };
        var markerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await storage.PutAsync(StorageStores.AppMetadata, new { Id = markerId });
        var original = new Exercise
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "Meine Brustpresse", Equipment = "Eigene Maschine", Note = "Eigene Notiz",
            MuscleGroups = [MuscleGroup.Brust],
            Steps = ["Eigener Schritt"], ImagePath = "images/exercises/custom.jpg", VideoUrl = "https://example.org/eigen"
        };
        // Store in the old JSON shape, without Tips, Mistakes or ImagePath2.
        await storage.PutAsync(StorageStores.Exercises, new
        {
            original.Id, original.Name, original.Equipment, original.Note, original.MuscleGroups,
            original.Steps, original.ImagePath, original.VideoUrl
        });
        var custom = new Exercise { Name = "Brustpresse" };
        await storage.PutAsync(StorageStores.Exercises, custom);

        await new TrainingDataInitializer(http, storage).InitializeAsync();

        var updated = (await storage.GetAsync<Exercise>(StorageStores.Exercises, original.Id))!;
        Assert.Equal(original.Name, updated.Name);
        Assert.Equal(original.Equipment, updated.Equipment);
        Assert.Equal(original.MuscleGroups, updated.MuscleGroups);
        Assert.Equal(original.Note, updated.Note);
        Assert.Equal(original.Steps, updated.Steps);
        Assert.Equal(original.ImagePath, updated.ImagePath);
        Assert.Equal(original.VideoUrl, updated.VideoUrl);
        Assert.Empty(updated.Tips);
        Assert.Empty(updated.Mistakes);
        Assert.Null(updated.ImagePath2);
        Assert.Empty((await storage.GetAsync<Exercise>(StorageStores.Exercises, custom.Id))!.Tips);
        Assert.Equal(1, (await storage.GetAsync<TrainingDataInitializer.SeedMarker>(StorageStores.AppMetadata, markerId))!.Version);
        Assert.Empty(await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans));
        Assert.Empty(await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems));
        Assert.Equal(2, (await storage.GetAllAsync<Exercise>(StorageStores.Exercises)).Count);
    }

    [Fact]
    public async Task UserDataWithoutSeedMarkersPreventsAllStartupWrites()
    {
        var storage = new InMemoryStorageService();
        var note = new Note { Text = "Vorhandene Daten ohne Seed-Marker" };
        await storage.PutAsync(StorageStores.Notes, note);
        storage.FailWrites = true;
        using var http = new HttpClient(new SeedFilesHandler()); // No base address: seed files must not be requested.
        await new TrainingDataInitializer(http, storage).InitializeAsync();
        Assert.Equal(note.Text, (await storage.GetAsync<Note>(StorageStores.Notes, note.Id))!.Text);
        Assert.Empty(await storage.GetAllAsync<Exercise>(StorageStores.Exercises));
        Assert.Empty(await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans));
        Assert.Empty(await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems));
        Assert.Empty(await storage.GetAllAsync<object>(StorageStores.AppMetadata));
    }

    [Fact]
    public void VideoSearchEscapesExerciseNameAndSearchPhrase()
    {
        var exercise = new Exercise { Name = "Rücken & Beine (KH)", VideoUrl = "http://example.org/video" };
        Assert.Equal("https://www.youtube.com/results?search_query=R%C3%BCcken%20%26%20Beine%20%28KH%29%20richtige%20Ausf%C3%BChrung",
            ExerciseGuidance.VideoLink(exercise));
        exercise.VideoUrl = "https://example.org/video";
        Assert.Equal(exercise.VideoUrl, ExerciseGuidance.VideoLink(exercise));
    }

    [Theory]
    [InlineData("https://example.org/video", true)]
    [InlineData(" HTTPS://example.org/video ", true)]
    [InlineData("http://example.org/video", false)]
    [InlineData("//example.org/video", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https:///video", false)]
    [InlineData(null, false)]
    public void VideoLinksRequireAbsoluteHttps(string? value, bool accepted) =>
        Assert.Equal(accepted, ExerciseGuidance.HttpsVideoUrl(value) is not null);

    [Fact]
    public async Task EditingExerciseRejectsInsecureVideoAndKeepsImagesAndMetadata()
    {
        var storage = new InMemoryStorageService();
        var exercise = new Exercise { Name = "Ergometer", Type = ExerciseType.Cardio, ImagePath = "images/exercises/example.jpg", Note = "Eigene Notiz" };
        await storage.PutAsync(StorageStores.Exercises, exercise);
        var service = new ExerciseService(storage);
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(exercise.Id, "Neuer Name", [], "", [], [], [], "http://example.org"));
        Assert.Equal(exercise.Name, (await service.GetAllAsync()).Single().Name);
        var saved = await service.UpdateAsync(exercise.Id, " Neues Ergometer ", [MuscleGroup.Cardio], "Ergometer",
            ["Schritt"], ["Tipp"], ["Fehler"], " https://example.org/video ");
        Assert.Equal("Neues Ergometer", saved.Name);
        Assert.Equal(exercise.Type, saved.Type);
        Assert.Equal(exercise.Note, saved.Note);
        Assert.Equal(exercise.ImagePath, saved.ImagePath);
        Assert.Equal("https://example.org/video", saved.VideoUrl);
        Assert.Equal(saved.Mistakes, (await service.GetAllAsync()).Single().Mistakes);
    }

    [Fact]
    public async Task ExerciseSearchCombinesNameAndMuscleFilter()
    {
        var storage = new InMemoryStorageService();
        await storage.PutAsync(StorageStores.Exercises, new Exercise { Name = "Brustpresse", MuscleGroups = [MuscleGroup.Brust, MuscleGroup.Trizeps] });
        await storage.PutAsync(StorageStores.Exercises, new Exercise { Name = "Beinpresse", MuscleGroups = [MuscleGroup.Beine] });
        var service = new ExerciseService(storage);
        Assert.Equal(2, (await service.SearchAsync("PRESSE", null)).Count);
        Assert.Equal("Brustpresse", Assert.Single(await service.SearchAsync(" presse ", MuscleGroup.Trizeps)).Name);
        Assert.Empty(await service.SearchAsync("Bein", MuscleGroup.Brust));
    }

    [Fact]
    public async Task PlanValidationAndReorderingPreserveExerciseConfiguration()
    {
        var storage = new InMemoryStorageService();
        var exercise = new Exercise { Name = "Beinpresse" };
        await storage.PutAsync(StorageStores.Exercises, exercise);
        var service = new WorkoutPlanService(storage);
        var plan = new WorkoutPlan { Name = "  Beintraining  ", Exercises = [new PlannedExercise { ExerciseId = exercise.Id, Sets = 4 }, new PlannedExercise { ExerciseId = exercise.Id, Sets = 2 }] };
        var original = plan.Exercises[0];
        service.MoveExercise(plan, 0, 1);
        Assert.Same(original, plan.Exercises[1]);
        service.MoveExercise(plan, 1, 1);
        Assert.Same(original, plan.Exercises[1]);
        await service.SaveAsync(plan);
        var restored = await service.GetAsync(plan.Id);
        Assert.Equal("Beintraining", restored!.Name);
        Assert.Equal(4, restored.Exercises[1].Sets);
        plan.Exercises[0].MaxReps = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(plan));
        plan.Exercises[0].MaxReps = 12;
        plan.Exercises[0].ExerciseId = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(plan));
        await service.DeleteAsync(plan.Id);
        Assert.Null(await service.GetAsync(plan.Id));
    }

    private sealed class SeedFilesHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var file = Path.GetFileName(request.RequestUri!.AbsolutePath);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", file), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
