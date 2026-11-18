using System.Text.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class UsdaProvider(HttpClient http, IProfileService profiles) : IFoodSearchProvider
{
    public const string ClientName = "USDA";
    public string Name => "USDA";
    public async Task<List<FoodItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var key = (await profiles.GetAsync()).UsdaApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new FoodSearchException("USDA ist erst mit einem API-Key in den Einstellungen verfügbar.");
        if (!SearchRequestPolicy.CanSearch(query)) return [];
        using var json = await FoodJson.ReadAsync(http, $"foods/search?api_key={Uri.EscapeDataString(key.Trim())}&query={Uri.EscapeDataString(query.Trim())}&dataType=Foundation,SR%20Legacy&pageSize=20", cancellationToken);
        return json.RootElement.TryGetProperty("foods", out var foods) && foods.ValueKind == JsonValueKind.Array
            ? foods.EnumerateArray().Select(Map).Where(f => f is not null).Cast<FoodItem>().ToList() : [];
    }
    public Task<FoodItem?> LookupBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => throw new FoodSearchException("Barcode-Suche wird über Open Food Facts angeboten.");
    public static FoodItem? Map(JsonElement food)
    {
        if (string.IsNullOrWhiteSpace(food.Text("description")) || string.IsNullOrWhiteSpace(food.Text("fdcId"))) return null;
        var values = new Dictionary<string, decimal?>();
        if (food.TryGetProperty("foodNutrients", out var nutrients) && nutrients.ValueKind == JsonValueKind.Array)
            foreach (var nutrient in nutrients.EnumerateArray()) values[nutrient.Text("nutrientNumber")] = nutrient.Number("value");
        decimal Get(string number) => values.GetValueOrDefault(number) ?? 0;
        return new FoodItem
        {
            Name = food.Text("description"), ExternalId = food.Text("fdcId"), Source = FoodSource.Usda,
            HasIncompleteNutrients = !values.TryGetValue("208", out var kcal) || kcal is null,
            NutrientsPer100G = new() { Kcal = Get("208"), ProteinGrams = Get("203"), CarbohydrateGrams = Get("205"), FatGrams = Get("204"), FiberGrams = Get("291"), SugarGrams = Get("269"), SaltGrams = Get("307") * 2.5m / 1000m }
        };
    }
}
