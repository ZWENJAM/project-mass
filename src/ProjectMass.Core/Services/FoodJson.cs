using System.Globalization;
using System.Text.Json;

namespace ProjectMass.Services;

internal static class FoodJson
{
    public static string Text(this JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "" : "";
    public static decimal? Number(this JsonElement element, string name)
    {
        var text = element.Text(name);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;
    }
    public static async Task<JsonDocument> ReadAsync(HttpClient http, string path, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(path, token);
            if ((int)response.StatusCode == 429) throw new FoodSearchException("Zu viele Anfragen beim Anbieter. Bitte warte eine Minute und versuche es erneut.");
            if ((int)response.StatusCode is 401 or 403) throw new FoodSearchException("Zugriff abgelehnt. Bitte prüfe bei USDA deinen API-Key.");
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new FoodSearchException("Die Anfrage dauerte länger als 10 Sekunden. Bitte erneut versuchen."); }
        catch (HttpRequestException) { throw new FoodSearchException("Anbieter nicht erreichbar. Bitte Verbindung prüfen oder eigene Lebensmittel nutzen."); }
        catch (JsonException) { throw new FoodSearchException("Der Anbieter hat eine ungültige Antwort geliefert."); }
    }
}
