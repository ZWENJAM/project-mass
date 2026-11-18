namespace ProjectMass.Services;

public interface ISyncTarget
{
    Task<string?> ReadAsync(string path);
    Task WriteAsync(string path, string content);
    Task<IReadOnlyList<string>> ListAsync(string folder);
    Task DeleteAsync(string path);
    Task TestAsync();

    async Task<bool> TryReplaceAsync(string path, string? expected, string content)
    {
        if (!string.Equals(await ReadAsync(path), expected, StringComparison.Ordinal)) return false;
        await WriteAsync(path, content);
        return true;
    }
}

public class SyncTargetException(string message) : Exception(message);

public sealed class FolderPermissionException() :
    SyncTargetException("Zugriff auf NAS-Ordner erneuern.");
