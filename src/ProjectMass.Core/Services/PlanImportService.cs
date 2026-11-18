using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class ImportFormatException(string message) : Exception(message);

public sealed record ImportedExercise(string Name, int Sets, int MinReps, int MaxReps, int RestSeconds,
    List<MuscleGroup> MuscleGroups, string Equipment, bool IsNew, List<string> Steps, List<string> Tips,
    List<string> Mistakes, string? VideoUrl);
public sealed record ImportedPlan(string Name, DayOfWeek? Day, string Note, List<ImportedExercise> Exercises);
public sealed record PlanImportPreview(List<ImportedPlan> Plans, List<string> Warnings);

public interface IPlanImportService
{
    Task<PlanImportPreview> PreviewAsync(string text);
    Task<int> ImportAsync(PlanImportPreview preview);
    string BuildPrompt(UserProfile profile, decimal? weightKg, IEnumerable<Exercise> catalog);
}

// Turns AI-written JSON (or a hand-written file) into training plans. Tolerant on input, strict on values.
public sealed class PlanImportService(IStorageService storage) : IPlanImportService
{
    public const string FormatName = "project-mass-plan";
    private const int MaxPlans = 20, MaxExercises = 30;

    public async Task<PlanImportPreview> PreviewAsync(string text)
    {
        var root = ImportJson.Parse(text);
        var planNodes = root switch
        {
            JsonArray array => array.ToList(),
            JsonObject o when o["plans"] is JsonArray planArray => planArray.ToList(),
            JsonObject o when o["exercises"] is JsonArray => new List<JsonNode?> { o },
            _ => throw new ImportFormatException("Kein Trainingsplan gefunden. Erwartet wird ein Objekt mit \"plans\".")
        };
        if (planNodes.Count == 0) throw new ImportFormatException("Die Datei enthält keine Pläne.");
        if (planNodes.Count > MaxPlans) throw new ImportFormatException($"Höchstens {MaxPlans} Pläne auf einmal.");

        var catalog = (await storage.GetAllAsync<Exercise>(StorageStores.Exercises))
            .Select(e => e.Name.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var warnings = new List<string>();
        var plans = new List<ImportedPlan>();
        foreach (var node in planNodes)
        {
            if (node is not JsonObject p) continue;
            var name = ImportJson.Text(p["name"]).Trim();
            if (name.Length is 0 or > 80) { warnings.Add("Ein Plan ohne gültigen Namen wurde übersprungen."); continue; }
            var exercises = new List<ImportedExercise>();
            foreach (var e in ImportJson.Items(p, "exercises").OfType<JsonObject>().Take(MaxExercises))
            {
                var exerciseName = ImportJson.Text(e["name"]).Trim();
                if (exerciseName.Length is 0 or > 80) { warnings.Add($"{name}: eine Übung ohne Namen wurde übersprungen."); continue; }
                var reps = e["reps"] is JsonValue v && v.TryGetValue<int>(out var single) ? single.ToString(CultureInfo.InvariantCulture) : ImportJson.Text(e["reps"]);
                var (min, max) = BackupService.ParseRepRange(reps);
                var groups = ImportJson.Items(e, "muscleGroups").Select(ImportJson.Text)
                    .Select(g => Enum.TryParse<MuscleGroup>(g.Trim(), true, out var group) ? group : (MuscleGroup?)null)
                    .Where(g => g.HasValue).Select(g => g!.Value).Distinct().ToList();
                var video = ImportJson.Text(e["videoUrl"]).Trim();
                var validVideo = ExerciseGuidance.HttpsVideoUrl(video);
                if (video.Length > 0 && validVideo is null)
                    warnings.Add($"{exerciseName}: Ungültiger HTTPS-Videolink übersprungen.");
                exercises.Add(new ImportedExercise(exerciseName,
                    (int)Math.Clamp(ImportJson.Number(e["sets"]) ?? 3, 1, 10), min, max,
                    (int)Math.Clamp(ImportJson.Number(e["restSeconds"]) ?? ImportJson.Number(e["rest"]) ?? 120, 30, 600),
                    groups, ImportJson.Text(e["equipment"]).Trim(), !catalog.Contains(exerciseName),
                    GuideLines(e, "steps", 6), GuideLines(e, "tips", 4), GuideLines(e, "mistakes", 3), validVideo));
            }
            if (exercises.Count == 0) { warnings.Add($"{name}: keine gültigen Übungen, Plan übersprungen."); continue; }
            plans.Add(new ImportedPlan(name, ParseDay(ImportJson.Text(p["day"])), ImportJson.Text(p["note"] ?? p["focus"]).Trim(), exercises));
        }
        if (plans.Count == 0) throw new ImportFormatException("Kein Plan konnte gelesen werden. " + string.Join(" ", warnings));
        return new PlanImportPreview(plans, warnings);
    }

    public async Task<int> ImportAsync(PlanImportPreview preview)
    {
        var catalog = (await storage.GetAllAsync<Exercise>(StorageStores.Exercises))
            .GroupBy(e => e.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
        var existingPlans = (await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans))
            .Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (var imported in preview.Plans)
        {
            var plan = new WorkoutPlan
            {
                Name = existingPlans.Contains(imported.Name) ? $"{imported.Name} (Import)" : imported.Name,
                Day = imported.Day,
                Note = imported.Note
            };
            foreach (var e in imported.Exercises)
            {
                var isNew = !catalog.TryGetValue(e.Name, out var exercise);
                exercise ??= new Exercise
                {
                    Name = e.Name, MuscleGroups = e.MuscleGroups, Equipment = e.Equipment,
                    Type = e.MuscleGroups.Contains(MuscleGroup.Cardio) ? ExerciseType.Cardio : ExerciseType.Strength,
                    Note = "Per Import angelegt"
                };
                var changed = ExerciseGuidance.FillMissing(exercise, new Exercise
                {
                    Steps = e.Steps, Tips = e.Tips, Mistakes = e.Mistakes, VideoUrl = e.VideoUrl
                });
                if (isNew || changed)
                {
                    await storage.PutAsync(StorageStores.Exercises, exercise);
                    catalog[e.Name] = exercise;
                }
                plan.Exercises.Add(new PlannedExercise { ExerciseId = exercise.Id, Sets = e.Sets, MinReps = e.MinReps, MaxReps = e.MaxReps, RestSeconds = e.RestSeconds });
            }
            await storage.PutAsync(StorageStores.WorkoutPlans, plan);
            existingPlans.Add(plan.Name);
        }
        return preview.Plans.Count;
    }

    public string BuildPrompt(UserProfile profile, decimal? weightKg, IEnumerable<Exercise> catalog)
    {
        var about = ImportJson.AboutMe(profile, weightKg);
        var names = string.Join(", ", catalog.Select(e => e.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        var groups = string.Join(", ", Enum.GetNames<MuscleGroup>());
        return $$"""
            Erstelle mir einen Trainingsplan für meine Trainings-App PROJECT MASS.

            Über mich: {{about}}
            Meine Wünsche: [HIER ERGÄNZEN, z. B. Fitnessstudio mit Maschinen, 60 Minuten pro Einheit, Schwerpunkt Oberkörper]

            Antworte ausschließlich mit gültigem JSON in genau diesem Format, ohne Erklärungen davor oder danach:
            {
              "format": "{{FormatName}}",
              "version": 1,
              "plans": [
                {
                  "name": "Oberkörper A",
                  "day": "Montag",
                  "note": "Brust, Rücken, Schultern",
                  "exercises": [
                    {
                      "name": "Bankdrücken", "sets": 3, "reps": "8-12", "restSeconds": 120,
                      "muscleGroups": ["Brust", "Trizeps"], "equipment": "Langhantel",
                      "steps": ["Stell beide Füße fest auf.", "Senk die Stange zur unteren Brust.", "Drück die Stange wieder hoch."],
                      "tips": ["Zieh die Schulterblätter zusammen.", "Halte die Handgelenke über den Ellbogen."],
                      "mistakes": ["Die Stange auf der Brust abfedern.", "Das Gesäß von der Bank heben."],
                      "videoUrl": null
                    }
                  ]
                }
              ]
            }

            Regeln:
            - Ein Plan pro Trainingstag. "day" ist ein deutscher Wochentag oder null.
            - "sets" 1 bis 10, "reps" als Bereich wie "8-12" oder als Zahl, "restSeconds" 30 bis 600.
            - "muscleGroups" nur aus: {{groups}}.
            - Optional pro Übung: "steps" (3 bis 6 Schritte), "tips" (2 bis 4 Tipps) und "mistakes" (2 bis 3 Fehler) als kurze deutsche Textlisten. "videoUrl" ist null oder ein direkter HTTPS-Link zu einem passenden Video. Erfinde keine Links.
            - Verwende nach Möglichkeit genau diese Übungsnamen aus meiner App: {{names}}.
            """;
    }

    private static List<string> GuideLines(JsonObject exercise, string field, int limit) =>
        ImportJson.Items(exercise, field).OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var line) ? line?.Trim() : null)
            .OfType<string>().Where(line => line.Length > 0).Take(limit).ToList();

    public static DayOfWeek? ParseDay(string text) => text.Trim().ToLowerInvariant() switch
    {
        "montag" or "mo" or "monday" => DayOfWeek.Monday,
        "dienstag" or "di" or "tuesday" => DayOfWeek.Tuesday,
        "mittwoch" or "mi" or "wednesday" => DayOfWeek.Wednesday,
        "donnerstag" or "do" or "thursday" => DayOfWeek.Thursday,
        "freitag" or "fr" or "friday" => DayOfWeek.Friday,
        "samstag" or "sa" or "saturday" => DayOfWeek.Saturday,
        "sonntag" or "so" or "sunday" => DayOfWeek.Sunday,
        _ => null
    };
}

// Shared helpers for both import formats.
public static class ImportJson
{
    // AI answers often wrap JSON in ```json fences or add a sentence before it: take the outermost JSON block.
    public static JsonNode Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ImportFormatException("Bitte JSON einfügen oder eine Datei wählen.");
        var start = text.IndexOfAny(new[] { '{', '[' });
        var end = Math.Max(text.LastIndexOf('}'), text.LastIndexOf(']'));
        if (start < 0 || end <= start) throw new ImportFormatException("Im Text wurde kein JSON gefunden.");
        try
        {
            return JsonNode.Parse(text[start..(end + 1)], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                ?? throw new ImportFormatException("Das JSON ist leer.");
        }
        catch (JsonException ex) { throw new ImportFormatException($"Das JSON ist fehlerhaft (Zeile {ex.LineNumber + 1}). Bitte die KI um gültiges JSON bitten."); }
    }

    public static IEnumerable<JsonNode?> Items(JsonNode node, string key) => node[key] is JsonArray array ? array : Enumerable.Empty<JsonNode?>();

    public static string Text(JsonNode? node) => node is JsonValue v
        ? v.TryGetValue<string>(out var text) ? text : v.TryGetValue<decimal>(out var number) ? number.ToString(CultureInfo.InvariantCulture) : ""
        : "";

    public static decimal? Number(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) && decimal.TryParse(s.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    public static string AboutMe(UserProfile profile, decimal? weightKg)
    {
        var parts = new List<string>();
        var goal = profile.Goal switch { FitnessGoal.Build => "Muskelaufbau", FitnessGoal.Lose => "Fett verlieren", _ => "Gewicht halten" };
        parts.Add($"Ziel: {goal}");
        if (profile.TrainingDaysPerWeek > 0) parts.Add($"{profile.TrainingDaysPerWeek} Trainingstage pro Woche");
        if (weightKg is { } w) parts.Add($"Gewicht {w.ToString("0.#", CultureInfo.GetCultureInfo("de-DE"))} kg");
        if (profile.HeightCm is { } h) parts.Add($"Größe {h:0} cm");
        if (profile.BirthYear is { } y) parts.Add($"Jahrgang {y}");
        if (profile.Gender is Gender.Male or Gender.Female) parts.Add(profile.Gender == Gender.Male ? "männlich" : "weiblich");
        return string.Join(", ", parts) + ".";
    }
}
