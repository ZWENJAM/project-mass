using System.Text.Json;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class FoodImageTests
{
    private static JsonElement Product(string imageJson) => JsonDocument.Parse($$"""
        { "code": "4002971243703", "product_name": "Skyr", "nutriments": { "energy-kcal_100g": 63 } {{imageJson}} }
        """).RootElement;

    [Fact]
    public void FrontImageIsPreferred()
    {
        var food = OpenFoodFactsProvider.Map(Product("""
            , "image_front_small_url": "https://images.openfoodfacts.org/images/products/400/front.200.jpg",
              "image_small_url": "https://images.openfoodfacts.org/images/products/400/other.200.jpg"
            """))!;
        Assert.Equal("https://images.openfoodfacts.org/images/products/400/front.200.jpg", food.ImageUrl);
    }

    [Fact]
    public void FallsBackToGeneralImageAndToNone()
    {
        Assert.EndsWith("other.200.jpg", OpenFoodFactsProvider.Map(Product("""
            , "image_small_url": "https://images.openfoodfacts.org/images/products/400/other.200.jpg"
            """))!.ImageUrl);
        Assert.Null(OpenFoodFactsProvider.Map(Product(""))!.ImageUrl);
    }

    [Theory]
    [InlineData("http://images.openfoodfacts.org/a.jpg")]
    [InlineData("https://evil.example/a.jpg")]
    [InlineData("https://openfoodfacts.org.evil.example/a.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void UnsafeImageUrlsAreDropped(string url) => Assert.Null(OpenFoodFactsProvider.SafeImageUrl(url));
}
