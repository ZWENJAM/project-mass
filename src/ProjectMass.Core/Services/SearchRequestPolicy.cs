namespace ProjectMass.Services;

public sealed class SearchRequestPolicy(TimeProvider clock)
{
    private readonly Queue<DateTimeOffset> searches = new();
    public static string Normalize(string query) => string.Join(' ', query.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    public static bool CanSearch(string query) => Normalize(query).Length >= 3;
    public Task DebounceAsync(CancellationToken token) => Task.Delay(TimeSpan.FromMilliseconds(500), clock, token);
    public void ReserveOffSearch()
    {
        lock (searches)
        {
            var now = clock.GetUtcNow();
            while (searches.TryPeek(out var oldest) && oldest <= now.AddMinutes(-1)) searches.Dequeue();
            if (searches.Count >= 10) throw new FoodSearchException("Du hast bereits 10 Suchanfragen in einer Minute gestellt. Bitte warte kurz; eigene Lebensmittel und gespeicherte Treffer bleiben verfügbar.");
            searches.Enqueue(now);
        }
    }
}
