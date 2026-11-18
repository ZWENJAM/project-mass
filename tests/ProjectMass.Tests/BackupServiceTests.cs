using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class BackupServiceTests
{
    private readonly InMemoryStorageService storage = new();
    private BackupService Backups => new(storage, new FixedClock());

    // Shape of the old Vanilla-PWA localStorage export (project-mass-v1).
    private const string LegacyJson = """
    {
      "version": 1,
      "settings": { "name": "Demoprofil", "kcal": 2300, "protein": 180, "weekly": 4 },
      "plans": [ { "id": "upper-a", "name": "Oberkörper A", "day": "Montag", "focus": "Brust", "exercises": [ { "name": "Brustpresse", "sets": 3, "reps": "8–12" } ] } ],
      "sessions": [ { "id": "6f1c2a7e-3b7d-4a55-9a0e-0c8f3a1d2b11", "planId": "upper-a", "name": "Oberkörper A", "date": "2026-10-01", "note": "lief gut",
        "exercises": [ { "name": "Brustpresse", "reps": "8–12", "sets": [ { "kg": 80, "reps": 10, "done": true }, { "kg": 82.5, "reps": 8, "done": false } ] },
                       { "name": "Exotische Übung", "reps": "10", "sets": [ { "kg": 20, "reps": 10, "done": true } ] } ] } ],
      "measurements": [ { "id": "m1", "date": "2026-10-02", "weight": 92.4, "waist": null, "chest": 0, "arm": 41, "fat": null } ],
      "food": [ { "id": "f1", "date": "2026-10-02", "name": "Skyr mit Beeren", "kcal": 200, "protein": 27 } ],
      "notes": [ { "id": "n1", "date": "2026-10-03", "text": "Schulter ok" } ],
      "draft": null
    }
    """;

    [Fact]
    public async Task CurrentBackupRoundTripsAndReplacesExistingData()
    {
        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Bleibt erhalten?" });
        await storage.PutAsync(StorageStores.BodyMeasurements, new BodyMeasurement { Date = new(2026, 11, 1), WeightKg = 90 });
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Demoprofil", TargetKcal = 2600 });
        var json = await Backups.ExportJsonAsync();

        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Nach dem Export angelegt" });
        await storage.ClearAsync(StorageStores.BodyMeasurements);

        var preview = Backups.Preview(json);
        Assert.Equal(BackupKind.Current, preview.Kind);
        Assert.Equal(1, preview.Measurements);

        await Backups.ImportJsonAsync(json);
        var notes = await storage.GetAllAsync<Note>(StorageStores.Notes);
        Assert.Equal("Bleibt erhalten?", Assert.Single(notes).Text);
        Assert.Equal(90m, Assert.Single(await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements)).WeightKg);
        Assert.Equal(2600m, (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!.TargetKcal);
    }

    [Fact]
    public async Task FailedImportRestoresPreviousData()
    {
        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Original" });
        var json = await Backups.ExportJsonAsync();
        var faulty = new FaultyStorage(storage);
        await Assert.ThrowsAnyAsync<Exception>(() => new BackupService(faulty, new FixedClock()).ImportJsonAsync(json));
        Assert.Equal("Original", Assert.Single(await storage.GetAllAsync<Note>(StorageStores.Notes)).Text);
    }

    [Fact]
    public async Task LegacyBackupIsMergedWithMappedValues()
    {
        await storage.PutAsync(StorageStores.Exercises, new Exercise { Name = "Brustpresse", MuscleGroups = [MuscleGroup.Brust] });
        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Vorhandene Notiz" });

        var preview = Backups.Preview(LegacyJson);
        Assert.Equal(BackupKind.Legacy, preview.Kind);

        var result = await Backups.ImportJsonAsync(LegacyJson);
        Assert.Equal((1, 1, 1, 1), (result.Sessions, result.Measurements, result.FoodEntries, result.Notes));

        var session = Assert.Single(await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions));
        Assert.True(session.IsCompleted);
        Assert.Equal(Guid.Parse("6f1c2a7e-3b7d-4a55-9a0e-0c8f3a1d2b11"), session.Id);
        var bench = session.Exercises[0];
        Assert.Equal((8, 12), (bench.MinReps, bench.MaxReps));
        Assert.Equal(new[] { 80m, 82.5m }, bench.Sets.Select(s => s.WeightKg).ToArray());
        Assert.Equal(new[] { true, false }, bench.Sets.Select(s => s.IsCompleted).ToArray());

        // Existing exercise is matched by name, the unknown one is added to the catalog.
        var exercises = await storage.GetAllAsync<Exercise>(StorageStores.Exercises);
        Assert.Equal(2, exercises.Count);
        Assert.Equal(exercises.Single(e => e.Name == "Brustpresse").Id, bench.ExerciseId);

        var measurement = Assert.Single(await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements));
        Assert.Equal(92.4m, measurement.WeightKg);
        Assert.Null(measurement.ChestCm); // 0 in the old app means "not measured"
        Assert.Equal(41m, measurement.UpperArmCm);

        var food = Assert.Single(await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries));
        Assert.Equal(200m, food.FoodSnapshot.NutrientsPer100G.Kcal * food.AmountGrams / 100m);
        Assert.Equal(27m, food.FoodSnapshot.NutrientsPer100G.ProteinGrams * food.AmountGrams / 100m);

        Assert.Equal(2, (await storage.GetAllAsync<Note>(StorageStores.Notes)).Count);
        var profile = (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!;
        Assert.Equal((2300m, 180m, 4, "Demoprofil"), (profile.TargetKcal, profile.TargetProteinGrams, profile.TrainingDaysPerWeek, profile.Name));
    }

    [Fact]
    public async Task LegacyImportTwiceCreatesNoDuplicatesAndKeepsExistingGoals()
    {
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { TargetKcal = 2800 });
        await Backups.ImportJsonAsync(LegacyJson);
        await Backups.ImportJsonAsync(LegacyJson);
        Assert.Single(await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions));
        Assert.Single(await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements));
        Assert.Single(await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries));
        Assert.Equal(2, (await storage.GetAllAsync<Exercise>(StorageStores.Exercises)).Count);
        Assert.Equal(2800m, (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!.TargetKcal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("""{"format":"etwas-anderes"}""")]
    [InlineData("""{"format":"project-mass","version":99}""")]
    public void InvalidFilesAreRejectedWithReadableMessage(string json) =>
        Assert.Throws<BackupFormatException>(() => Backups.Preview(json));

    [Theory]
    [InlineData("8–12", 8, 12)]
    [InlineData("12-8", 8, 12)]
    [InlineData("10", 10, 10)]
    [InlineData("", 8, 12)]
    public void RepRangesFromOldPlansAreParsed(string text, int min, int max) =>
        Assert.Equal((min, max), BackupService.ParseRepRange(text));

    [Fact]
    public void LegacyIdsAreStableForNonGuidIds() =>
        Assert.Equal(BackupService.LegacyId("food", "f1"), BackupService.LegacyId("food", "f1"));

    [Fact]
    public async Task MeasurementCsvUsesSemicolonsAndDecimalCommas()
    {
        await storage.PutAsync(StorageStores.BodyMeasurements, new BodyMeasurement { Date = new(2026, 11, 2), WeightKg = 92.45m, BodyFatPercent = 18 });
        var lines = (await Backups.ExportMeasurementsCsvAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Datum;Gewicht (kg);Bauch (cm);Brust (cm);Oberarm (cm);KFA (%)", lines[0]);
        Assert.Equal("2026-11-02;92,45;;;;18", lines[1]);
    }

    [Fact]
    public async Task TrainingCsvQuotesTextWithSeparators()
    {
        await storage.PutAsync(StorageStores.WorkoutSessions, new WorkoutSession
        {
            Name = "Push; schwer", Date = new(2026, 11, 3), IsCompleted = true,
            Exercises = [new SessionExercise { Name = "Bankdrücken", Sets = [new SetEntry { WeightKg = 82.5m, Reps = 8, IsCompleted = true }] }]
        });
        var lines = (await Backups.ExportTrainingCsvAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2026-11-03;\"Push; schwer\";Bankdrücken;1;Arbeit;82,5;8;;ja", lines[1]);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    }

    // Lets the clear succeed but fails on the first write, like a full disk mid-import.
    private sealed class FaultyStorage(InMemoryStorageService inner) : IStorageService
    {
        private bool failed;
        public Task<T?> GetAsync<T>(string store, Guid id) => inner.GetAsync<T>(store, id);
        public Task<T?> GetAsync<T>(string store, string id) => inner.GetAsync<T>(store, id);
        public Task<List<T>> GetAllAsync<T>(string store) => inner.GetAllAsync<T>(store);
        public Task PutAsync<T>(string store, T item)
        {
            if (!failed && store == StorageStores.Notes) { failed = true; throw new InvalidOperationException("Speicher voll"); }
            return inner.PutAsync(store, item);
        }
        public Task DeleteAsync(string store, Guid id) => inner.DeleteAsync(store, id);
        public Task ClearAsync(string store) => inner.ClearAsync(store);
    }
}
