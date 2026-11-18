using System.Text.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class OpenFoodFactsProvider(HttpClient http, SearchRequestPolicy policy) : IFoodSearchProvider
{
    public const string ClientName = "OpenFoodFacts";
    public const string Fields = "code,product_name,product_name_de,brands,nutriments,serving_quantity,serving_size,image_front_small_url,image_small_url,categories_tags";
    public string Name => "Open Food Facts";
    public async Task<List<FoodItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!SearchRequestPolicy.CanSearch(query)) return [];
        cancellationToken.ThrowIfCancellationRequested();
        policy.ReserveOffSearch();
        using var json = await FoodJson.ReadAsync(http, $"cgi/search.pl?search_terms={Uri.EscapeDataString(query.Trim())}&search_simple=1&action=process&json=1&page_size=20&lc=de&fields={Fields}&app_name=ProjectMass&app_version=2.0", cancellationToken);
        return json.RootElement.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array
            ? products.EnumerateArray().Select(Map).Where(f => f is not null).Cast<FoodItem>().ToList() : [];
    }
    public async Task<FoodItem?> LookupBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (!BarcodeValidator.IsValid(barcode)) throw new FoodSearchException("Bitte einen Barcode mit 8–14 Ziffern und gültiger Prüfziffer eingeben.");
        barcode = BarcodeValidator.Normalize(barcode);
        using var json = await FoodJson.ReadAsync(http, $"api/v2/product/{barcode}.json?fields={Fields}&app_name=ProjectMass&app_version=2.0", cancellationToken);
        if (json.RootElement.TryGetProperty("status", out var status) && status.ToString() == "0") return null;
        var food = json.RootElement.TryGetProperty("product", out var product) ? Map(product) : null;
        if (food is not null) { food.Barcode = barcode; food.ExternalId ??= barcode; }
        return food;
    }
    public static FoodItem? Map(JsonElement product)
    {
        var name = product.Text("product_name_de");
        if (string.IsNullOrWhiteSpace(name)) name = product.Text("product_name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = product.TryGetProperty("nutriments", out var nutrients) && nutrients.ValueKind == JsonValueKind.Object ? nutrients : JsonSerializer.SerializeToElement(new { });
        var kcal = n.Number("energy-kcal_100g") ?? (n.Number("energy-kj_100g") is { } kj ? kj / 4.184m : null);
        var food = new FoodItem
        {
            Category = product.TryGetProperty("categories_tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? FoodCategories.FromOffTags(tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!))
                : FoodCategory.Unknown,
            ImageUrl = SafeImageUrl(product.Text("image_front_small_url")) ?? SafeImageUrl(product.Text("image_small_url")),
            Name = name.Trim(), Brand = product.Text("brands"), Barcode = product.Text("code"), ExternalId = product.Text("code"), Source = FoodSource.OpenFoodFacts,
            HasIncompleteNutrients = kcal is null,
            NutrientsPer100G = new() { Kcal = kcal ?? 0, ProteinGrams = n.Number("proteins_100g") ?? 0, CarbohydrateGrams = n.Number("carbohydrates_100g") ?? 0,
                FatGrams = n.Number("fat_100g") ?? 0, FiberGrams = n.Number("fiber_100g") ?? 0, SugarGrams = n.Number("sugars_100g") ?? 0, SaltGrams = n.Number("salt_100g") ?? 0 }
        };
        if (product.Number("serving_quantity") is > 0 and var grams)
            food.PortionSizes.Add(new() { Name = string.IsNullOrWhiteSpace(product.Text("serving_size")) ? "1 Portion" : product.Text("serving_size"), Grams = grams });
        return food;
    }

    // Only HTTPS images from Open Food Facts are shown. Anything else in the data is ignored.
    public static string? SafeImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host == "openfoodfacts.org" || uri.Host.EndsWith(".openfoodfacts.org", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri : null;
}
