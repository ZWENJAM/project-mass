using System.Net;
using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class NutritionSeedTests
{
    [Fact]
    public async Task SnackSeedPreservesOriginalPortionValuesAndDoesNotRecreateDeletedFood()
    {
        var storage = new InMemoryStorageService();
        using var http = new HttpClient(new SeedHandler()) { BaseAddress = new Uri("http://localhost/") };
        await new NutritionDataInitializer(http, storage).InitializeAsync();
        var foods = await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems);
        Assert.Equal(6, foods.Count);
        var expected = new[] { (150m, 20m), (200m, 27m), (120m, 23m), (200m, 25m), (400m, 35m), (125m, 30m) };
        var calculator = new NutritionCalculator();
        var ordered = foods.OrderBy(f => f.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var portion = Assert.Single(ordered[i].PortionSizes);
            var values = calculator.ForAmount(ordered[i].NutrientsPer100G, portion.Grams);
            Assert.InRange(values.Kcal, expected[i].Item1 - .000001m, expected[i].Item1 + .000001m);
            Assert.InRange(values.ProteinGrams, expected[i].Item2 - .000001m, expected[i].Item2 + .000001m);
            Assert.NotEmpty(ordered[i].Note);
        }
        Assert.Contains("200 g angenommen", ordered[4].Note);
        await storage.DeleteAsync(StorageStores.FoodItems, ordered[0].Id);
        ordered[1].Name = "Eigene Anpassung";
        await storage.PutAsync(StorageStores.FoodItems, ordered[1]);
        await new NutritionDataInitializer(http, storage).InitializeAsync();
        Assert.Equal(5, (await storage.GetAllAsync<FoodItem>(StorageStores.FoodItems)).Count);
        Assert.Equal("Eigene Anpassung", (await storage.GetAsync<FoodItem>(StorageStores.FoodItems, ordered[1].Id))!.Name);
    }

    [Theory]
    [InlineData("exercises.json")]
    [InlineData("workout-plans.json")]
    [InlineData("snacks.json")]
    public async Task SeedsAreUtf8WithoutBom(string name)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", name));
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
        _ = System.Text.Json.JsonDocument.Parse(bytes);
    }

    private sealed class SeedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", "snacks.json"), cancellationToken))
        };
    }
}
