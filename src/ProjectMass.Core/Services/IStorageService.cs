namespace ProjectMass.Services;

public interface IStorageService
{
    Task<T?> GetAsync<T>(string store, Guid id);
    Task<T?> GetAsync<T>(string store, string id);
    Task<List<T>> GetAllAsync<T>(string store);
    Task PutAsync<T>(string store, T item);
    Task DeleteAsync(string store, Guid id);
    Task ClearAsync(string store);
}
