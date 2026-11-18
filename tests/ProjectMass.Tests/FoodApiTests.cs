using System.Net;
using System.Text.Json;
using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class FoodApiTests
{
    [Fact]
    public async Task AndroidOffUsesFullTextAndMapsSearchALiciousHit()
    {
        var handler = new FixtureHandler("off-full-text.json");
        using var http = OffClient(handler);
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System), fullText: true);

        var food = Assert.Single(await provider.SearchAsync("harzer käse"));

        Assert.Equal("Harzer Käse", food.Name);
        Assert.Equal("Ja!", food.Brand);
        Assert.Equal("4388860049183", food.Barcode);
        Assert.Equal(FoodCategory.Dairy, food.Category);
        Assert.Equal(new Nutrients { Kcal = 125, ProteinGrams = 30, CarbohydrateGrams = .1m, FatGrams = .5m, SaltGrams = 4 },
            food.NutrientsPer100G);
        Assert.StartsWith("https://images.openfoodfacts.org/", food.ImageUrl);
        Assert.Equal("https://search.openfoodfacts.org/search?q=harzer%20k%C3%A4se&langs=de&page_size=20",
            Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task AndroidOffFallsBackToTagsWhenFullTextFails()
    {
        var handler = new FixtureHandler("off-search.json", unavailableHost: "search.openfoodfacts.org");
        using var http = OffClient(handler);
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System), fullText: true);

        Assert.NotEmpty(await provider.SearchAsync("Skyr"));
        Assert.Collection(handler.Urls,
            url => Assert.StartsWith("https://search.openfoodfacts.org/search?", url),
            url => Assert.StartsWith("https://world.openfoodfacts.org/api/v2/search?categories_tags_de=skyr", url));
    }

    [Fact]
    public async Task OffMapsGermanNameAllNutrientsPortionKjFallbackAndMissingValues()
    {
        var handler = new FixtureHandler("off-search.json");
        using var http = OffClient(handler);
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System));
        var foods = await provider.SearchAsync("Skyr & Natur");
        Assert.Equal(4, foods.Count);
        var food = foods[0];
        Assert.Equal("Skyr natur", food.Name); Assert.Equal("Testmarke", food.Brand);
        Assert.Equal(FoodSource.OpenFoodFacts, food.Source);
        Assert.Equal("4002971243703", food.Barcode);
        Assert.Equal(new Nutrients { Kcal = 63, ProteinGrams = 11, CarbohydrateGrams = 4, FatGrams = .2m, SugarGrams = 4, SaltGrams = .1m }, food.NutrientsPer100G);
        Assert.Equal(150, Assert.Single(food.PortionSizes).Grams);
        Assert.False(food.HasIncompleteNutrients);
        Assert.Equal(100, foods[1].NutrientsPer100G.Kcal);
        Assert.Equal(3.25m, foods[1].NutrientsPer100G.ProteinGrams);
        Assert.True(foods[2].HasIncompleteNutrients);
        Assert.Equal(0, foods[2].NutrientsPer100G.FatGrams);
        Assert.False(foods[3].HasIncompleteNutrients); // Explicit 0 kcal is a complete value.
        var url = Assert.Single(handler.Urls);
        Assert.Contains("api/v2/search?categories_tags_de=skyr-natur", url);
        Assert.Contains("countries_tags_en=germany", url);
        Assert.Contains("page_size=20&lc=de", url);
        Assert.Contains("app_name=ProjectMass&app_version=2.0", url);
        Assert.Contains("serving_quantity,serving_size", url);
    }

    [Fact]
    public async Task OffBarcodeLookupValidatesBeforeHttpAndMapsFixture()
    {
        var handler = new FixtureHandler("off-barcode.json");
        using var http = OffClient(handler);
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System));
        await Assert.ThrowsAsync<FoodSearchException>(() => provider.LookupBarcodeAsync("4002971243704"));
        Assert.Empty(handler.Urls);
        var food = await provider.LookupBarcodeAsync("4002971243703");
        Assert.Equal("Barcode-Testprodukt", food!.Name);
        Assert.Contains("api/v2/product/4002971243703.json?fields=", Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task UsdaRequiresLocalKeyAndMapsNutrientNumbersIncludingSodium()
    {
        var storage = new InMemoryStorageService();
        var profiles = new ProfileService(storage, new NutritionCalculator(), TimeProvider.System);
        var handler = new FixtureHandler("usda-search.json");
        using var http = new HttpClient(handler) { BaseAddress = new("https://api.nal.usda.gov/fdc/v1/"), Timeout = TimeSpan.FromSeconds(10) };
        var provider = new UsdaProvider(http, profiles);
        await Assert.ThrowsAsync<FoodSearchException>(() => provider.SearchAsync("Yogurt"));
        Assert.Empty(handler.Urls);
        var testKey = Guid.NewGuid().ToString("N");
        var profile = await profiles.GetAsync(); profile.UsdaApiKey = testKey; await profiles.SaveAsync(profile);
        var foods = await provider.SearchAsync("Yogurt");
        Assert.Equal(2, foods.Count);
        Assert.Equal("12345", foods[0].ExternalId); Assert.Equal(FoodSource.Usda, foods[0].Source);
        Assert.Equal(new Nutrients { Kcal = 63, ProteinGrams = 5.25m, CarbohydrateGrams = 7, FatGrams = 1.55m, FiberGrams = .4m, SugarGrams = 6, SaltGrams = .1m }, foods[0].NutrientsPer100G); // 40 mg * 2.5 / 1000 = 0.1 g salt.
        Assert.True(foods[1].HasIncompleteNutrients);
        Assert.Contains($"api_key={testKey}", Assert.Single(handler.Urls));
        Assert.Contains("dataType=Foundation,SR%20Legacy&pageSize=20", handler.Urls[0]);
    }

    [Theory]
    [InlineData("4002971243703", true)]
    [InlineData("4002971243704", false)]
    [InlineData("96385074", true)]
    [InlineData("036000291452", true)]
    [InlineData("0036000291452", true)]
    [InlineData("10012345000017", true)]
    [InlineData("04252614", true)] // UPC-E -> UPC-A 042100005264.
    [InlineData("123", false)]
    [InlineData("400297124370x", false)]
    [InlineData("４００２９７１２４３７０３", false)]
    public void BarcodeChecksum(string barcode, bool valid) => Assert.Equal(valid, BarcodeValidator.IsValid(barcode));

    [Fact]
    public void UpcEExpandsBeforeLookupAndDeduplication() => Assert.Equal("042100005264", BarcodeValidator.Normalize("04252614"));

    [Fact]
    public async Task ImportDedupesByBarcodeOrSourceAndExternalIdAndKeepsLocalCorrections()
    {
        var storage = new InMemoryStorageService(); var service = new FoodService(storage);
        var first = await service.ImportAsync(new() { Name = "API-Produkt", Source = FoodSource.OpenFoodFacts, Barcode = "036000291452", ExternalId = "one", NutrientsPer100G = new() { Kcal = 30 } });
        first.Name = "Eigene Korrektur"; first.IsFavorite = true; await service.SaveAsync(first);
        var duplicate = await service.ImportAsync(new() { Name = "Andere Schreibweise", Source = FoodSource.OpenFoodFacts, Barcode = "0036000291452", ExternalId = "two" });
        Assert.Equal(first.Id, duplicate.Id); Assert.Equal("Eigene Korrektur", duplicate.Name); Assert.True(duplicate.IsFavorite);
        var usda = await service.ImportAsync(new() { Name = "USDA", Source = FoodSource.Usda, ExternalId = "123" });
        var again = await service.ImportAsync(new() { Name = "USDA erneut", Source = FoodSource.Usda, ExternalId = "123" });
        Assert.Equal(usda.Id, again.Id);
        await service.ImportAsync(new() { Name = "OFF gleiche externe Nummer", Source = FoodSource.OpenFoodFacts, ExternalId = "123" });
        Assert.Equal(3, (await service.SearchAsync()).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(new() { Name = "Unvollständig", Source = FoodSource.OpenFoodFacts, HasIncompleteNutrients = true }));
    }

    [Fact]
    public async Task CancellationIsForwardedAndNotConvertedToConnectionError()
    {
        var handler = new CancellingHandler(); using var http = OffClient(handler);
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System));
        using var cancellation = new CancellationTokenSource();
        var search = provider.SearchAsync("Skyr", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.True(handler.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task HttpRateLimitProducesFriendlyError()
    {
        using var http = OffClient(new FixtureHandler("off-search.json", HttpStatusCode.TooManyRequests));
        var provider = new OpenFoodFactsProvider(http, new(TimeProvider.System));
        var error = await Assert.ThrowsAsync<FoodSearchException>(() => provider.SearchAsync("Skyr"));
        Assert.Contains("Zu viele Anfragen", error.Message);
    }
    private static HttpClient OffClient(HttpMessageHandler handler) => new(handler) { BaseAddress = new("https://world.openfoodfacts.org/"), Timeout = TimeSpan.FromSeconds(10) };
    internal sealed class FixtureHandler(string fixture, HttpStatusCode status = HttpStatusCode.OK, string? unavailableHost = null) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.Host == unavailableHost) return new(HttpStatusCode.ServiceUnavailable);
            return new(status) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), cancellationToken)) };
        }
    }
    private sealed class CancellingHandler : HttpMessageHandler
    {
        public CancellationToken Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Token = cancellationToken; await Task.Delay(Timeout.Infinite, cancellationToken); return new(HttpStatusCode.OK); }
    }
}
