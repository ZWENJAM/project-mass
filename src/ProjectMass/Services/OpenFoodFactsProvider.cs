using System.Text.Json;
using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class OpenFoodFactsProvider(HttpClient http, SearchRequestPolicy policy) : IFoodSearchProvider
{
    public const string ClientName = "OpenFoodFacts";
    public const string Fields = "code,product_name,product_name_de,brands,nutriments,serving_quantity,serving_size,image_front_small_url,image_small_url,categories_tags";
    public string Name => "Open Food Facts";
    // Open Food Facts no longer serves its full-text search (cgi/search.pl) to anonymous browser apps, and the new
    // search service sends no CORS header. The v2 API still works from the browser, but only filters by tags:
    // so the query is matched as product type (category) first and as brand second. Popular German products first.
    public async Task<List<FoodItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!SearchRequestPolicy.CanSearch(query)) return [];
        var words = SearchRequestPolicy.Normalize(query).Split(' ').Select(Tag).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0) return [];
        var slug = string.Join('-', words);

        var foods = await SearchTagsAsync($"categories_tags_de={slug}", cancellationToken);
        if (foods.Count > 0) return foods;
        // "ehrmann skyr" → brand ehrmann + category skyr; a single word may be a brand ("milram").
        var filter = words.Length > 1
            ? $"brands_tags={words[0]}&categories_tags_de={string.Join('-', words[1..])}"
            : $"brands_tags={slug}";
        return await SearchTagsAsync(filter, cancellationToken);
    }

    private async Task<List<FoodItem>> SearchTagsAsync(string filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        policy.ReserveOffSearch();
        JsonDocument json;
        try
        {
            json = await FoodJson.ReadAsync(http, $"api/v2/search?{filter}&countries_tags_en=germany&sort_by=unique_scans_n&page_size=20&lc=de&fields={Fields}&app_name=ProjectMass&app_version=2.0", cancellationToken);
        }
        catch (FoodSearchException ex) when (ex.Message.StartsWith("Anbieter nicht erreichbar"))
        {
            // When Open Food Facts is busy or rate-limits, its error page has no CORS header and shows up as a network error.
            throw new FoodSearchException("Open Food Facts antwortet gerade nicht (oft: zu viele Anfragen). Bitte eine Minute warten oder den Barcode scannen.");
        }
        using (json)
            return json.RootElement.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array
                ? products.EnumerateArray().Select(Map).Where(f => f is not null).Cast<FoodItem>().ToList() : [];
    }

    // Tags are lowercase with hyphens; umlauts stay (categories_tags_de=käse works, kase does not).
    private static string Tag(string text) => Uri.EscapeDataString(new string(text.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()));

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
