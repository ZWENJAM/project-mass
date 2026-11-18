using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed record FoodSearchResult(List<FoodItem> Foods, string? Message = null);

public sealed class FoodSearchService(OpenFoodFactsProvider off, UsdaProvider usda, SearchRequestPolicy policy,
    SearchCacheService cache, IBrowserConnectivity connectivity, IFoodService foods, IProfileService profiles)
{
    public async Task<FoodSearchResult> SearchAsync(FoodSource source, string query, CancellationToken token)
    {
        if (!SearchRequestPolicy.CanSearch(query)) return new([], "Bitte mindestens 3 Zeichen eingeben.");
        await policy.DebounceAsync(token);
        if (source == FoodSource.Usda && string.IsNullOrWhiteSpace((await profiles.GetAsync()).UsdaApiKey))
            throw new FoodSearchException("USDA ist erst mit einem API-Key verfügbar.");
        return await Execute(source, query, ct => Provider(source).SearchAsync(query, ct), token);
    }
    public Task<FoodSearchResult> LookupBarcodeAsync(string code, CancellationToken token)
    {
        if (!BarcodeValidator.IsValid(code)) throw new FoodSearchException("Bitte 8–14 Ziffern mit gültiger Prüfziffer eingeben.");
        code = BarcodeValidator.Normalize(code);
        return Execute(FoodSource.OpenFoodFacts, $"barcode:{code}", async ct =>
        {
            var food = await off.LookupBarcodeAsync(code, ct);
            return food is null ? [] : [food];
        }, token);
    }
    private IFoodSearchProvider Provider(FoodSource source) => source switch
    {
        FoodSource.OpenFoodFacts => off, FoodSource.Usda => usda, _ => throw new ArgumentException("Unbekannter Suchanbieter.")
    };
    private async Task<FoodSearchResult> Execute(FoodSource source, string query, Func<CancellationToken, Task<List<FoodItem>>> request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var cached = await cache.GetAsync(source, query);
        var online = await connectivity.IsOnlineAsync();
        token.ThrowIfCancellationRequested();
        if (cached is not null) return new(cached, online ? "Gespeicherte Suchergebnisse (max. 7 Tage alt)." : "Offline: gespeicherte Suchergebnisse. Eigene Lebensmittel bleiben verfügbar.");
        if (!online) return new(await foods.SearchAsync(query.StartsWith("barcode:") ? null : query), "Du bist offline. Kein gültiger Cache-Treffer; angezeigt werden eigene Lebensmittel.");
        try
        {
            var result = await request(token);
            token.ThrowIfCancellationRequested();
            string? message = result.Count == 0 ? "Kein Produkt gefunden." : null;
            try { await cache.PutAsync(source, query, result); }
            catch (Exception) { message = "Ergebnisse geladen, Suchcache konnte nicht gespeichert werden."; }
            return new(result, message);
        }
        catch (FoodSearchException ex)
        {
            token.ThrowIfCancellationRequested();
            return new(await foods.SearchAsync(query.StartsWith("barcode:") ? null : query), ex.Message + " Angezeigt werden passende eigene Lebensmittel.");
        }
    }
}
