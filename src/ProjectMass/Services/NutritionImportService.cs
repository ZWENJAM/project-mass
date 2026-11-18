using System.Globalization;
using System.Text.Json.Nodes;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed record ImportedTargets(decimal? Kcal, decimal? ProteinGrams, decimal? CarbohydrateGrams, decimal? FatGrams, int? WaterMl);
public sealed record NutritionImportPreview(ImportedTargets? Targets, List<FoodItem> Foods, List<Recipe> Recipes, List<string> Tips, List<string> Warnings);
public sealed record NutritionImportResult(int Foods, int SkippedFoods, int Recipes, bool TargetsApplied, bool TipsSaved);

public interface INutritionImportService
{
    Task<NutritionImportPreview> PreviewAsync(string text);
    Task<NutritionImportResult> ImportAsync(NutritionImportPreview preview, bool applyTargets);
    string BuildPrompt(UserProfile profile, decimal? weightKg);
}

// Imports AI-written nutrition advice: daily targets, foods, recipes and tips.
public sealed class NutritionImportService(IStorageService storage, IRecipeService recipes, TimeProvider clock) : INutritionImportService
{
    public const string FormatName = "project-mass-nutrition";

    public async Task<NutritionImportPreview> PreviewAsync(string text)
    {
        if (ImportJson.Parse(text) is not JsonObject root) throw new ImportFormatException("Erwartet wird ein JSON-Objekt mit \"targets\", \"foods\", \"recipes\" oder \"tips\".");
        var warnings = new List<string>();

        ImportedTargets? targets = null;
        if (root["targets"] is JsonObject targetNode)
        {
            targets = new ImportedTargets(InRange(targetNode["kcal"], 800, 10000), InRange(targetNode["protein"], 0, 600), InRange(targetNode["carbs"], 0, 1500), InRange(targetNode["fat"], 0, 500),
                InRange(targetNode["waterMl"], 500, 10000) is { } water ? (int)water : null);
            if (targets is { Kcal: null, ProteinGrams: null, CarbohydrateGrams: null, FatGrams: null, WaterMl: null }) { targets = null; warnings.Add("Die Tagesziele enthalten keine gültigen Werte."); }
        }

        var foods = new List<FoodItem>();
        foreach (var f in ImportJson.Items(root, "foods").OfType<JsonObject>().Take(200))
        {
            var name = ImportJson.Text(f["name"]).Trim();
            if (name.Length is 0 or > 180) { warnings.Add("Ein Lebensmittel ohne Namen wurde übersprungen."); continue; }
            if (f["per100g"] is not JsonObject n || InRange(n["kcal"], 0, 950) is not { } kcal) { warnings.Add($"„{name}“: Kalorien pro 100 g fehlen oder sind unplausibel, übersprungen."); continue; }
            var food = new FoodItem
            {
                Name = name,
                Brand = ImportJson.Text(f["brand"]).Trim(),
                Note = "Per Import angelegt",
                Category = Enum.TryParse<FoodCategory>(ImportJson.Text(f["category"]), true, out var category) ? category : FoodCategory.Unknown,
                NutrientsPer100G = new Nutrients
                {
                    Kcal = kcal, ProteinGrams = InRange(n["protein"], 0, 100) ?? 0, CarbohydrateGrams = InRange(n["carbs"], 0, 100) ?? 0,
                    FatGrams = InRange(n["fat"], 0, 100) ?? 0, FiberGrams = InRange(n["fiber"], 0, 100) ?? 0,
                    SugarGrams = InRange(n["sugar"], 0, 100) ?? 0, SaltGrams = InRange(n["salt"], 0, 100) ?? 0
                }
            };
            foreach (var portion in ImportJson.Items(f, "portions").OfType<JsonObject>().Take(10))
                if (ImportJson.Text(portion["name"]).Trim() is { Length: > 0 and <= 60 } portionName && InRange(portion["grams"], 0.1m, 5000) is { } grams)
                    food.PortionSizes.Add(new PortionSize { Name = portionName, Grams = grams });
            foods.Add(food);
        }

        // Ingredients may refer to foods from this import or to foods already in the app.
        var known = foods.Concat(await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems))
            .GroupBy(f => f.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
        var recipeList = new List<Recipe>();
        foreach (var r in ImportJson.Items(root, "recipes").OfType<JsonObject>().Take(50))
        {
            var name = ImportJson.Text(r["name"]).Trim();
            if (name.Length is 0 or > 180) { warnings.Add("Ein Rezept ohne Namen wurde übersprungen."); continue; }
            var recipe = new Recipe
            {
                Name = name,
                Portions = (int)Math.Clamp(ImportJson.Number(r["portions"]) ?? 1, 1, 50),
                Instructions = ImportJson.Text(r["instructions"]).Trim()
            };
            foreach (var i in ImportJson.Items(r, "ingredients").OfType<JsonObject>())
            {
                var foodName = ImportJson.Text(i["food"]).Trim();
                if (!known.TryGetValue(foodName, out var food)) { warnings.Add($"„{name}“: Zutat „{foodName}“ unbekannt, ausgelassen."); continue; }
                if (InRange(i["grams"], 0.1m, 10000) is not { } grams) { warnings.Add($"„{name}“: Menge für „{foodName}“ fehlt, ausgelassen."); continue; }
                recipe.Ingredients.Add(new RecipeIngredient { FoodSnapshot = food, AmountGrams = grams });
            }
            if (recipe.Ingredients.Count == 0) { warnings.Add($"„{name}“: keine gültigen Zutaten, übersprungen."); continue; }
            recipeList.Add(recipe);
        }

        var tips = ImportJson.Items(root, "tips").Select(ImportJson.Text).Select(tip => tip.Trim()).Where(tip => tip.Length is > 0 and <= 1000).Take(30).ToList();

        if (targets is null && foods.Count == 0 && recipeList.Count == 0 && tips.Count == 0)
            throw new ImportFormatException("Nichts Importierbares gefunden. " + string.Join(" ", warnings));
        return new NutritionImportPreview(targets, foods, recipeList, tips, warnings);
    }

    public async Task<NutritionImportResult> ImportAsync(NutritionImportPreview preview, bool applyTargets)
    {
        // Foods the user already has keep their own values; only new names are added.
        var existing = (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems))
            .GroupBy(f => f.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
        int added = 0, skipped = 0;
        foreach (var food in preview.Foods)
        {
            if (existing.ContainsKey(food.Name)) { skipped++; continue; }
            await storage.PutAsync(StorageStores.FoodItems, food);
            existing[food.Name] = food;
            added++;
        }

        foreach (var recipe in preview.Recipes) await recipes.SaveAsync(recipe);

        var targetsApplied = false;
        if (applyTargets && preview.Targets is { } target)
        {
            var profile = await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId) ?? new UserProfile();
            if (target.Kcal is { } kcal) profile.TargetKcal = Math.Round(kcal);
            if (target.ProteinGrams is { } protein) profile.TargetProteinGrams = Math.Round(protein);
            if (target.CarbohydrateGrams is { } carbs) profile.TargetCarbohydrateGrams = Math.Round(carbs);
            if (target.FatGrams is { } fat) profile.TargetFatGrams = Math.Round(fat);
            if (target.WaterMl is { } water) profile.TargetWaterMl = water;
            profile.Id = UserProfile.SingletonId;
            await storage.PutAsync(StorageStores.UserProfiles, profile);
            targetsApplied = true;
        }

        var tipsSaved = false;
        if (preview.Tips.Count > 0)
        {
            await storage.PutAsync(StorageStores.Notes, new Note
            {
                Date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
                Text = "Ernährungsempfehlungen\n" + string.Join("\n", preview.Tips.Select(tip => "- " + tip))
            });
            tipsSaved = true;
        }
        return new NutritionImportResult(added, skipped, preview.Recipes.Count, targetsApplied, tipsSaved);
    }

    public string BuildPrompt(UserProfile profile, decimal? weightKg)
    {
        var about = ImportJson.AboutMe(profile, weightKg);
        var current = profile.TargetKcal > 0
            ? $"Meine aktuellen Tagesziele: {profile.TargetKcal:0} kcal, {profile.TargetProteinGrams:0} g Protein, {profile.TargetCarbohydrateGrams:0} g Kohlenhydrate, {profile.TargetFatGrams:0} g Fett."
            : "Ich habe noch keine Tagesziele festgelegt.";
        var categories = string.Join(", ", Enum.GetNames<FoodCategory>().Where(c => c != nameof(FoodCategory.Unknown)));
        return $$"""
            Erstelle mir Ernährungsempfehlungen für meine Trainings-App PROJECT MASS.

            Über mich: {{about}}
            {{current}}
            Meine Wünsche: [HIER ERGÄNZEN, z. B. viel Protein, schnelle Rezepte unter 20 Minuten, kein Fisch, Budget-Einkauf]

            Antworte ausschließlich mit gültigem JSON in genau diesem Format, ohne Erklärungen davor oder danach:
            {
              "format": "{{FormatName}}",
              "version": 1,
              "targets": { "kcal": 2600, "protein": 180, "carbs": 300, "fat": 80, "waterMl": 3000 },
              "foods": [
                { "name": "Skyr natur", "brand": "", "category": "Dairy",
                  "per100g": { "kcal": 63, "protein": 11, "carbs": 4, "fat": 0.2, "fiber": 0, "sugar": 4, "salt": 0.1 },
                  "portions": [ { "name": "1 Becher", "grams": 450 } ] }
              ],
              "recipes": [
                { "name": "Skyr-Bowl mit Beeren", "portions": 1, "instructions": "Alles in eine Schüssel geben.",
                  "ingredients": [ { "food": "Skyr natur", "grams": 300 } ] }
              ],
              "tips": [ "Verteile das Protein auf 4 Mahlzeiten mit je 30 bis 45 g." ]
            }

            Regeln:
            - Alle Nährwerte pro 100 g, realistische Durchschnittswerte für Produkte aus deutschen Supermärkten.
            - Jede Zutat eines Rezepts muss unter "foods" stehen, mit exakt gleichem Namen.
            - "category" nur aus: {{categories}}.
            - "targets" nur angeben, wenn du neue Tagesziele empfiehlst. Alle Abschnitte sind optional.
            - "tips" kurz und konkret, höchstens 10.
            """;
    }

    private static decimal? InRange(JsonNode? node, decimal min, decimal max) =>
        ImportJson.Number(node) is { } value && value >= min && value <= max ? value : null;
}
