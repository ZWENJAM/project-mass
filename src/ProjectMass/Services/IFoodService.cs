using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IFoodService
{
    Task<FoodItem?> GetAsync(Guid id);
    Task<List<FoodItem>> SearchAsync(string? query = null, bool favoritesOnly = false);
    Task<List<FoodItem>> GetRecentAsync();
    Task SaveAsync(FoodItem food);
    Task<FoodItem> ImportAsync(FoodItem food);
    Task DeleteAsync(Guid id);
    Task SetFavoriteAsync(Guid id, bool favorite);
}
