using System.Globalization;
using ProjectMass.Services;

namespace ProjectMass.Mobile;

internal sealed class BackupFiles(SafDocuments documents)
{
    internal const long MaximumBytes = 30 * 1024 * 1024;
    private const string LatestName = "project-mass-backup.json";
    private const string DailyPrefix = "project-mass-";
    private static string InternalFolder => Path.Combine(FileSystem.AppDataDirectory, "backups");

    public async Task WriteInternalAsync(string json, DateOnly date)
    {
        Directory.CreateDirectory(InternalFolder);
        var name = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json";
        var path = Path.Combine(InternalFolder, name);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }

        foreach (var old in OlderBackups(Directory.EnumerateFiles(InternalFolder).Select(Path.GetFileName).OfType<string>(), ""))
            File.Delete(Path.Combine(InternalFolder, old));
    }

    public async Task WriteExternalAsync(string json, DateOnly date)
    {
        if (documents.Folder is not { } folder) return;
        var dayFile = DailyPrefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json";
        var files = documents.List(folder);
        await documents.UpsertAsync(folder, files, dayFile, json);
        await documents.UpsertAsync(folder, files, LatestName, json);
        foreach (var old in OlderBackups(files.Keys.Append(dayFile), DailyPrefix)) documents.Delete(files[old]);
    }

    public IEnumerable<string> InternalCandidates() =>
        Directory.Exists(InternalFolder) ? Directory.EnumerateFiles(InternalFolder, "*.json").OrderDescending() : [];

    public IEnumerable<global::Android.Net.Uri> ExternalCandidates()
    {
        if (documents.Folder is not { } folder) return [];
        var files = documents.List(folder);
        return files.Where(pair => pair.Key == LatestName || IsDaily(pair.Key, DailyPrefix))
            .OrderByDescending(pair => pair.Key == LatestName).ThenByDescending(pair => pair.Key).Select(pair => pair.Value);
    }

    public static async Task<string> ReadAsync(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[32 * 1024];
        while (await stream.ReadAsync(bytes) is > 0 and var size)
        {
            if (buffer.Length + size > limit) throw new BackupFormatException("Die Datei ist größer als 30 MB.");
            buffer.Write(bytes, 0, size);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return await reader.ReadToEndAsync();
    }

    private static IEnumerable<string> OlderBackups(IEnumerable<string> names, string prefix) =>
        names.Distinct(StringComparer.Ordinal).Where(name => IsDaily(name, prefix)).OrderDescending(StringComparer.Ordinal).Skip(14);

    private static bool IsDaily(string file, string prefix) =>
        file.Length == prefix.Length + 15 && file.StartsWith(prefix, StringComparison.Ordinal) &&
        file.EndsWith(".json", StringComparison.Ordinal) && DateOnly.TryParseExact(file.AsSpan(prefix.Length, 10),
            "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
