using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectMass.Models;

namespace ProjectMass.Services;

public enum BackupKind { Current, Legacy }

public sealed record BackupPreview(BackupKind Kind, int Sessions, int Measurements, int FoodEntries, int Notes, DateTimeOffset? ExportedAt);

public sealed record BackupImportResult(BackupKind Kind, int Sessions, int Measurements, int FoodEntries, int Notes);

public sealed class BackupFormatException(string message) : Exception(message);

// Full snapshot of all user data. The search cache is deliberately excluded: it can be rebuilt.
public sealed class BackupData
{
    public string Format { get; set; } = BackupService.FormatName;
    public int Version { get; set; } = BackupService.CurrentVersion;
    public DateTimeOffset ExportedAt { get; set; }
    public UserProfile? Profile { get; set; }
    public List<Exercise> Exercises { get; set; } = [];
    public List<WorkoutPlan> WorkoutPlans { get; set; } = [];
    public List<WorkoutSession> WorkoutSessions { get; set; } = [];
    public List<FoodItem> FoodItems { get; set; } = [];
    public List<FoodLogEntry> FoodLogEntries { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
    public List<BodyMeasurement> BodyMeasurements { get; set; } = [];
    public List<WaterLog> WaterLogs { get; set; } = [];
    public List<Note> Notes { get; set; } = [];
    public List<TrainingDataInitializer.SeedMarker> AppMetadata { get; set; } = [];
}

public interface IBackupService
{
    Task<string> ExportJsonAsync();
    BackupPreview Preview(string json);
    Task<BackupImportResult> ImportJsonAsync(string json);
    Task<string> ExportMeasurementsCsvAsync();
    Task<string> ExportTrainingCsvAsync();
    Task MarkBackupAsync();
}

public sealed class BackupService(IStorageService storage, TimeProvider clock) : IBackupService
{
    public const string FormatName = "project-mass";
    public const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> ExportJsonAsync()
    {
        var data = await ReadAllAsync();
        data.ExportedAt = clock.GetUtcNow();
        return JsonSerializer.Serialize(data, Options);
    }

    public BackupPreview Preview(string json)
    {
        var root = Parse(json);
        if (IsCurrent(root))
        {
            var data = DeserializeCurrent(json);
            return new(BackupKind.Current, data.WorkoutSessions.Count(s => s.IsCompleted), data.BodyMeasurements.Count, data.FoodLogEntries.Count, data.Notes.Count, data.ExportedAt);
        }
        if (IsLegacy(root))
            return new(BackupKind.Legacy, Count(root, "sessions"), Count(root, "measurements"), Count(root, "food"), Count(root, "notes"), null);
        throw new BackupFormatException("Diese Datei ist kein PROJECT MASS-Backup.");
    }

    public async Task<BackupImportResult> ImportJsonAsync(string json)
    {
        if (storage is ChangeTrackingStorageService tracked)
            return await tracked.ImportLocalAsync(raw => new BackupService(raw, clock).ImportJsonAsync(json));
        var root = Parse(json);
        if (IsCurrent(root)) return await ImportCurrentAsync(DeserializeCurrent(json));
        if (IsLegacy(root)) return await ImportLegacyAsync((JsonObject)root);
        throw new BackupFormatException("Diese Datei ist kein PROJECT MASS-Backup.");
    }

    // Remembered so the home screen can remind about overdue backups.
    public async Task MarkBackupAsync()
    {
        var profile = await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId) ?? new UserProfile();
        profile.Id = UserProfile.SingletonId;
        profile.LastBackupAt = clock.GetUtcNow();
        await storage.PutAsync(StorageStores.UserProfiles, profile);
    }

    internal async Task<bool> TryImportForSyncAsync(string json, DateTimeOffset? expectedChange)
    {
        var data = DeserializeCurrent(json);
        var localProfile = await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId);
        if (data.Profile is not null || localProfile?.UsdaApiKey is not null)
        {
            data.Profile ??= new UserProfile();
            data.Profile.UsdaApiKey = localProfile?.UsdaApiKey;
        }
        if (storage is IndexedDbStorageService indexed)
            return await indexed.ReplaceForSyncAsync(data, expectedChange);
        await ImportCurrentAsync(data);
        return true;
    }

    // ---------- Current format: replaces everything ----------

    private async Task<BackupImportResult> ImportCurrentAsync(BackupData data)
    {
        // Keep the old state in memory so a failed write can be rolled back.
        var previous = await ReadAllAsync();
        try { await ReplaceAllAsync(data); }
        catch
        {
            try { await ReplaceAllAsync(previous); } catch { /* Rollback is best effort. The error below still surfaces. */ }
            throw;
        }
        return new(BackupKind.Current, data.WorkoutSessions.Count(s => s.IsCompleted), data.BodyMeasurements.Count, data.FoodLogEntries.Count, data.Notes.Count);
    }

    private async Task<BackupData> ReadAllAsync() => new()
    {
        Profile = await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId),
        Exercises = await storage.GetAllAsync<Exercise>(StorageStores.Exercises),
        WorkoutPlans = await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans),
        WorkoutSessions = await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions),
        FoodItems = await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems),
        FoodLogEntries = await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries),
        Recipes = await storage.GetAllAsync<Recipe>(StorageStores.Recipes),
        BodyMeasurements = await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements),
        WaterLogs = await storage.GetAllAsync<WaterLog>(StorageStores.WaterLogs),
        Notes = await storage.GetAllAsync<Note>(StorageStores.Notes),
        AppMetadata = await storage.GetAllAsync<TrainingDataInitializer.SeedMarker>(StorageStores.AppMetadata)
    };

    private async Task ReplaceAllAsync(BackupData data)
    {
        await ReplaceAsync(StorageStores.UserProfiles, data.Profile is null ? new List<UserProfile>() : new List<UserProfile> { data.Profile });
        await ReplaceAsync(StorageStores.Exercises, data.Exercises);
        await ReplaceAsync(StorageStores.WorkoutPlans, data.WorkoutPlans);
        await ReplaceAsync(StorageStores.WorkoutSessions, data.WorkoutSessions);
        await ReplaceAsync(StorageStores.FoodItems, data.FoodItems);
        await ReplaceAsync(StorageStores.FoodLogEntries, data.FoodLogEntries);
        await ReplaceAsync(StorageStores.Recipes, data.Recipes);
        await ReplaceAsync(StorageStores.BodyMeasurements, data.BodyMeasurements);
        await ReplaceAsync(StorageStores.WaterLogs, data.WaterLogs);
        await ReplaceAsync(StorageStores.Notes, data.Notes);
        await ReplaceAsync(StorageStores.AppMetadata, data.AppMetadata);
    }

    private async Task ReplaceAsync<T>(string store, List<T> items)
    {
        await storage.ClearAsync(store);
        foreach (var item in items) await storage.PutAsync(store, item);
    }

    private static BackupData DeserializeCurrent(string json)
    {
        BackupData? data;
        try { data = JsonSerializer.Deserialize<BackupData>(json, Options); }
        catch (JsonException) { throw new BackupFormatException("Das Backup ist beschädigt und kann nicht gelesen werden."); }
        if (data is null) throw new BackupFormatException("Das Backup ist leer.");
        if (data.Version > CurrentVersion) throw new BackupFormatException("Dieses Backup stammt aus einer neueren App-Version. Bitte zuerst die App aktualisieren.");
        if (data.Profile is not null) data.Profile.Id = UserProfile.SingletonId;
        var ids = new IEnumerable<Guid>[]
        {
            data.Exercises.Select(x => x.Id), data.WorkoutPlans.Select(x => x.Id), data.WorkoutSessions.Select(x => x.Id),
            data.FoodItems.Select(x => x.Id), data.FoodLogEntries.Select(x => x.Id), data.Recipes.Select(x => x.Id),
            data.BodyMeasurements.Select(x => x.Id), data.WaterLogs.Select(x => x.Id), data.Notes.Select(x => x.Id)
        };
        if (ids.Any(list => list.Any(id => id == Guid.Empty)))
            throw new BackupFormatException("Das Backup enthält Einträge ohne gültige ID.");
        return data;
    }

    // ---------- Legacy format (Vanilla-PWA „project-mass-v1“): merges, never deletes ----------

    private async Task<BackupImportResult> ImportLegacyAsync(JsonObject root)
    {
        var exercises = (await storage.GetAllAsync<Exercise>(StorageStores.Exercises))
            .GroupBy(e => e.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);

        var sessions = 0;
        foreach (var node in Items(root, "sessions"))
        {
            if (node is not JsonObject s || !TryDate(s["date"], out var date)) continue;
            var session = new WorkoutSession
            {
                Id = LegacyId("session", Text(s["id"])),
                Name = Text(s["name"]) is { Length: > 0 } name ? name : "Training",
                Date = date,
                Note = Text(s["note"]),
                IsCompleted = true,
                StartedAt = Noon(date),
                CompletedAt = Noon(date)
            };
            foreach (var exerciseNode in Items(s, "exercises"))
            {
                if (exerciseNode is not JsonObject e) continue;
                var exerciseName = Text(e["name"]).Trim();
                if (exerciseName.Length == 0) continue;
                if (!exercises.TryGetValue(exerciseName, out var exercise))
                {
                    // Unknown exercises are added to the catalog so history, PRs and pre-filling keep working.
                    exercise = new Exercise { Id = LegacyId("exercise", exerciseName.ToLowerInvariant()), Name = exerciseName, Type = ExerciseType.Strength, Note = "Aus altem Backup übernommen" };
                    await storage.PutAsync(StorageStores.Exercises, exercise);
                    exercises[exerciseName] = exercise;
                }
                var (min, max) = ParseRepRange(Text(e["reps"]));
                var sessionExercise = new SessionExercise { ExerciseId = exercise.Id, Name = exercise.Name, MinReps = min, MaxReps = max };
                foreach (var setNode in Items(e, "sets"))
                {
                    if (setNode is not JsonObject t) continue;
                    sessionExercise.Sets.Add(new SetEntry
                    {
                        WeightKg = Math.Clamp(Number(t["kg"]) ?? 0, 0, 2000),
                        Reps = (int)Math.Clamp(Math.Round(Number(t["reps"]) ?? 0), 0, 999),
                        IsCompleted = t["done"] is JsonValue done && done.TryGetValue<bool>(out var isDone) && isDone,
                        Type = SetType.Working
                    });
                }
                if (sessionExercise.Sets.Count > 0) session.Exercises.Add(sessionExercise);
            }
            await storage.PutAsync(StorageStores.WorkoutSessions, session);
            sessions++;
        }

        var measurements = 0;
        foreach (var node in Items(root, "measurements"))
        {
            if (node is not JsonObject m || !TryDate(m["date"], out var date)) continue;
            var measurement = new BodyMeasurement
            {
                Id = LegacyId("measurement", Text(m["id"])), Date = date,
                WeightKg = Positive(m["weight"]), WaistCm = Positive(m["waist"]), ChestCm = Positive(m["chest"]),
                UpperArmCm = Positive(m["arm"]), BodyFatPercent = Positive(m["fat"]) is { } fat && fat <= 100 ? fat : null
            };
            if (measurement.WeightKg is null && measurement.WaistCm is null && measurement.ChestCm is null && measurement.UpperArmCm is null && measurement.BodyFatPercent is null) continue;
            await storage.PutAsync(StorageStores.BodyMeasurements, measurement);
            measurements++;
        }

        var foodEntries = 0;
        foreach (var node in Items(root, "food"))
        {
            if (node is not JsonObject f || !TryDate(f["date"], out var date)) continue;
            var id = LegacyId("food", Text(f["id"]));
            // The old app stored absolute values per entry. Stored as 100 g of a snapshot with these values, the totals stay exact.
            await storage.PutAsync(StorageStores.FoodLogEntries, new FoodLogEntry
            {
                Id = id, Date = date, Meal = MealType.Snack, AmountGrams = 100, LoggedAt = Noon(date),
                FoodSnapshot = new FoodItem
                {
                    Id = id, Name = Text(f["name"]) is { Length: > 0 } name ? name : "Eintrag aus altem Backup",
                    Note = "Aus altem Backup: nur kcal und Protein bekannt",
                    NutrientsPer100G = new Nutrients { Kcal = Math.Max(0, Number(f["kcal"]) ?? 0), ProteinGrams = Math.Max(0, Number(f["protein"]) ?? 0) }
                }
            });
            foodEntries++;
        }

        var notes = 0;
        foreach (var node in Items(root, "notes"))
        {
            if (node is not JsonObject n || !TryDate(n["date"], out var date) || Text(n["text"]).Trim() is not { Length: > 0 } text) continue;
            await storage.PutAsync(StorageStores.Notes, new Note { Id = LegacyId("note", Text(n["id"])), Date = date, Text = text });
            notes++;
        }

        await MergeLegacySettingsAsync(root["settings"] as JsonObject);
        return new(BackupKind.Legacy, sessions, measurements, foodEntries, notes);
    }

    // Old goals are only applied where the new profile has none yet.
    private async Task MergeLegacySettingsAsync(JsonObject? settings)
    {
        if (settings is null) return;
        var profile = await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId) ?? new UserProfile();
        var changed = false;
        if (string.IsNullOrWhiteSpace(profile.Name) && Text(settings["name"]).Trim() is { Length: > 0 and <= 80 } name) { profile.Name = name; changed = true; }
        if (profile.TargetKcal <= 0 && Positive(settings["kcal"]) is { } kcal) { profile.TargetKcal = kcal; changed = true; }
        if (profile.TargetProteinGrams <= 0 && Positive(settings["protein"]) is { } protein) { profile.TargetProteinGrams = protein; changed = true; }
        if (profile.TrainingDaysPerWeek <= 0 && Positive(settings["weekly"]) is { } weekly && weekly <= 7) { profile.TrainingDaysPerWeek = (int)weekly; changed = true; }
        if (!changed) return;
        profile.Id = UserProfile.SingletonId;
        await storage.PutAsync(StorageStores.UserProfiles, profile);
    }

    // ---------- CSV ----------

    public async Task<string> ExportMeasurementsCsvAsync()
    {
        var rows = (await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements)).OrderBy(m => m.Date);
        var csv = new StringBuilder("Datum;Gewicht (kg);Bauch (cm);Brust (cm);Oberarm (cm);KFA (%)\r\n");
        foreach (var m in rows)
            csv.Append(string.Join(';', m.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Csv(m.WeightKg), Csv(m.WaistCm), Csv(m.ChestCm), Csv(m.UpperArmCm), Csv(m.BodyFatPercent))).Append("\r\n");
        return "﻿" + csv;
    }

    public async Task<string> ExportTrainingCsvAsync()
    {
        var sessions = (await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions)).Where(s => s.IsCompleted).OrderBy(s => s.Date).ThenBy(s => s.CompletedAt);
        var csv = new StringBuilder("Datum;Training;Übung;Satz;Typ;Gewicht (kg);Wdh.;RPE;Erledigt\r\n");
        foreach (var session in sessions)
            foreach (var exercise in session.Exercises)
                for (var i = 0; i < exercise.Sets.Count; i++)
                {
                    var set = exercise.Sets[i];
                    var type = set.Type switch { SetType.WarmUp => "Aufwärmen", SetType.Drop => "Drop", _ => "Arbeit" };
                    csv.Append(string.Join(';', session.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CsvText(session.Name), CsvText(exercise.Name),
                        (i + 1).ToString(CultureInfo.InvariantCulture), type, Csv(set.WeightKg), set.Reps.ToString(CultureInfo.InvariantCulture), Csv(set.Rpe), set.IsCompleted ? "ja" : "nein")).Append("\r\n");
                }
        return "﻿" + csv;
    }

    // German Excel expects ';' separators and decimal commas.
    private static string Csv(decimal? value) => value is null ? "" : value.Value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
    private static string CsvText(string value) => value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    // ---------- Helpers ----------

    private static JsonNode Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new BackupFormatException("Die Datei ist leer.");
        try { return JsonNode.Parse(json) ?? throw new BackupFormatException("Die Datei ist leer."); }
        catch (JsonException) { throw new BackupFormatException("Die Datei ist kein gültiges JSON."); }
    }

    private static bool IsCurrent(JsonNode root) =>
        root is JsonObject o && o["format"] is JsonValue v && v.TryGetValue<string>(out var format) && format == FormatName;

    private static bool IsLegacy(JsonNode root) =>
        root is JsonObject o && o["version"] is JsonValue v && v.TryGetValue<int>(out var version) && version == 1 && o["sessions"] is JsonArray && o["plans"] is JsonArray;

    private static int Count(JsonNode root, string key) => root[key] is JsonArray array ? array.Count : 0;
    private static IEnumerable<JsonNode?> Items(JsonNode node, string key) => node[key] is JsonArray array ? array : Enumerable.Empty<JsonNode?>();
    private static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";

    private static decimal? Number(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var d)) return d;
        if (v.TryGetValue<double>(out var dbl) && double.IsFinite(dbl)) return (decimal)dbl;
        return null;
    }

    private static decimal? Positive(JsonNode? node) => Number(node) is > 0 and var value ? value : null;

    private static bool TryDate(JsonNode? node, out DateOnly date) =>
        DateOnly.TryParseExact(Text(node), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTimeOffset Noon(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public static (int Min, int Max) ParseRepRange(string text)
    {
        var numbers = text.Split(new[] { '–', '-', '—', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
            .Where(n => n is > 0 and <= 999).Select(n => n!.Value).ToList();
        return numbers.Count switch
        {
            0 => (8, 12),
            1 => (numbers[0], numbers[0]),
            _ => (Math.Min(numbers[0], numbers[1]), Math.Max(numbers[0], numbers[1]))
        };
    }

    // Old IDs are usually UUIDs. Anything else becomes a stable GUID, so importing twice never duplicates.
    public static Guid LegacyId(string kind, string id)
    {
        if (Guid.TryParse(id, out var guid) && guid != Guid.Empty) return guid;
        var key = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"project-mass-v1:{kind}:{key}")).AsSpan(0, 16));
    }
}
