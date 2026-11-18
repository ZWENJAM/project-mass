using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IFoodSearchProvider
{
    string Name { get; }
    Task<List<FoodItem>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<FoodItem?> LookupBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
}

public sealed class FoodSearchException(string message) : Exception(message);
