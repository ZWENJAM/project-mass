using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectMass.Models;

namespace ProjectMass.Services;

public enum NasTargetKind { Folder, WebDav }
public enum NasSyncStatus { Synced, Pending, WaitingForWorkout, Conflict }
public enum SyncResolution { TakeNas, UploadLocal }

public sealed record SyncRevision(Guid DeviceId, string DeviceName, DateTimeOffset SavedAt);
public sealed record NasSyncResult(NasSyncStatus Status, SyncRevision? Conflict = null, bool Imported = false, string? Error = null);
public sealed record ConflictChoice(SyncRevision Revision, SyncResolution Resolution);

public sealed record NasSyncSettings
{
    public static readonly Guid Key = Guid.Parse("00000000-0000-0000-0000-000000000010");
    public Guid Id { get; init; } = Key;
    public Guid DeviceId { get; init; } = Guid.NewGuid();
    public string DeviceName { get; init; } = "PC";
    public NasTargetKind? Target { get; init; }
    public string FolderName { get; init; } = "";
    public string WebDavUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    // Acknowledge only the local change captured in the upload or guarded import.
    public DateTimeOffset? LastSyncAt { get; init; }
    public DateTimeOffset? LastCompletedAt { get; init; }
    public DateTimeOffset? LastRemoteSavedAt { get; init; }
    public string? LastRemoteDeviceName { get; init; }
    public DateOnly? LastDailyBackup { get; init; }
}

public sealed class SyncService(ChangeTrackingStorageService storage, TimeProvider clock)
{
    public const string CurrentPath = "project-mass/sync.json";
    public const string BackupFolder = "project-mass/backups";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly BackupService backups = new(storage.Inner, clock);

    public Task<NasSyncSettings?> GetSettingsAsync() =>
        storage.GetAsync<NasSyncSettings>(StorageStores.SyncMetadata, NasSyncSettings.Key);

    public Task SaveSettingsAsync(NasSyncSettings settings) => storage.PutAsync(StorageStores.SyncMetadata, settings);

    public async Task ResetTargetAsync(NasSyncSettings settings)
    {
        // A newly selected location may already contain another copy of our data.
        if (settings.LastCompletedAt.HasValue && await storage.GetChangeAsync() is null)
        {
            await storage.PutAsync(StorageStores.SyncMetadata, new LocalSyncChange { At = clock.GetUtcNow() });
        }

        await SaveSettingsAsync(settings with
        {
            LastSyncAt = null, LastCompletedAt = null, LastRemoteSavedAt = null,
            LastRemoteDeviceName = null, LastDailyBackup = null
        });
    }

    public async Task<NasSyncResult> SyncAsync(ISyncTarget target, ConflictChoice? choice = null)
    {
        await gate.WaitAsync();
        try
        {
            var settings = await GetSettingsAsync() ?? throw new InvalidOperationException("NAS-Sync ist nicht eingerichtet.");
            var remoteContent = await target.ReadAsync(CurrentPath);
            var remote = remoteContent is null ? null : ReadRemote(remoteContent);
            var local = await CaptureAsync();
            var remoteNewer = remote is not null && remote.Revision.DeviceId != settings.DeviceId &&
                (settings.LastRemoteSavedAt is null || remote.Revision.SavedAt > settings.LastRemoteSavedAt);
            var localChanged = local.ChangedAt.HasValue &&
                (settings.LastSyncAt is null || local.ChangedAt > settings.LastSyncAt);
            var conflict = remoteNewer && localChanged;

            if (remoteNewer && local.HasDraft) return new(NasSyncStatus.WaitingForWorkout);
            if (conflict && (choice is null || choice.Revision != remote!.Revision))
                return new(NasSyncStatus.Conflict, remote!.Revision);
            // An explicit decision only applies to the exact conflict that was shown.
            if (choice is not null && !conflict) return new(NasSyncStatus.Pending);

            var takeNas = remoteNewer && (!conflict || choice!.Resolution == SyncResolution.TakeNas);
            var upload = remote is null || (!takeNas && localChanged);
            var canonical = remote?.Backup ?? local.Backup;
            var revision = remote?.Revision;
            if (takeNas)
            {
                if (conflict) await SaveConflictAsync(target, local.Backup, settings.DeviceName);
                if (!string.Equals(await target.ReadAsync(CurrentPath), remoteContent, StringComparison.Ordinal))
                    return new(NasSyncStatus.Pending);
                var applied = await storage.WithLocalDataAsync(async () =>
                {
                    if (await HasDraftAsync()) return NasSyncStatus.WaitingForWorkout;
                    if ((await storage.GetChangeAsync())?.At != local.ChangedAt) return NasSyncStatus.Pending;
                    return await backups.TryImportForSyncAsync(canonical, local.ChangedAt)
                        ? NasSyncStatus.Synced : NasSyncStatus.Pending;
                });
                if (applied != NasSyncStatus.Synced) return new(applied);
            }
            else if (upload)
            {
                if (conflict) await SaveConflictAsync(target, remote!.Backup, remote.Revision.DeviceName);
                var now = clock.GetUtcNow();
                if (remote?.Revision.SavedAt >= now) now = remote.Revision.SavedAt.AddMilliseconds(1);
                if (settings.LastRemoteSavedAt >= now) now = settings.LastRemoteSavedAt.Value.AddMilliseconds(1);
                revision = new(settings.DeviceId, settings.DeviceName, now);
                canonical = local.Backup;
                var payload = JsonNode.Parse(canonical)!.AsObject();
                payload["sync"] = JsonSerializer.SerializeToNode(revision, Json);
                if (!await target.TryReplaceAsync(CurrentPath, remoteContent, payload.ToJsonString(Json)))
                    return new(NasSyncStatus.Pending);
            }

            // Save the checkpoint before maintenance so a failed daily backup cannot replay a completed import.
            var checkpoint = settings with
            {
                LastSyncAt = local.ChangedAt ?? DateTimeOffset.MinValue,
                LastRemoteSavedAt = takeNas || upload ? revision?.SavedAt : settings.LastRemoteSavedAt,
                LastRemoteDeviceName = takeNas || upload ? revision?.DeviceName : settings.LastRemoteDeviceName
            };
            await SaveSettingsAsync(checkpoint);
            var today = DateOnly.FromDateTime(clock.GetLocalNow().Date);
            if (checkpoint.LastDailyBackup != today)
            {
                try { await MaintainDailyBackupAsync(target, canonical, today); }
                catch (SyncTargetException ex)
                {
                    return new(NasSyncStatus.Pending, Imported: takeNas, Error: $"Tagesbackup fehlgeschlagen. {ex.Message}");
                }
                checkpoint = checkpoint with { LastDailyBackup = today };
            }
            var completed = clock.GetUtcNow();
            await MarkBackupAsync(completed);
            await SaveSettingsAsync(checkpoint with { LastCompletedAt = completed });
            var newerLocal = (await storage.GetChangeAsync())?.At > checkpoint.LastSyncAt;
            return new(newerLocal ? NasSyncStatus.Pending : NasSyncStatus.Synced, Imported: takeNas);
        }
        finally { gate.Release(); }
    }

    private Task<LocalSnapshot> CaptureAsync() => storage.WithLocalDataAsync(async () =>
    {
        var change = (await storage.GetChangeAsync())?.At;
        var draft = await HasDraftAsync();
        var data = JsonNode.Parse(await backups.ExportJsonAsync())!.AsObject();
        if (data["profile"] is JsonObject profile) profile.Remove("usdaApiKey");
        if ((await storage.GetChangeAsync())?.At != change)
            throw new SyncTargetException("Während des Abgleichs wurden lokale Daten geändert. Starte den Sync erneut.");
        return new LocalSnapshot(data.ToJsonString(Json), change, draft);
    });

    private async Task<bool> HasDraftAsync() =>
        (await storage.Inner.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions)).Any(s => !s.IsCompleted);

    private RemoteSnapshot ReadRemote(string content)
    {
        try
        {
            if (JsonNode.Parse(content) is not JsonObject node || node["sync"] is not JsonObject metadata)
                throw new JsonException();
            var revision = metadata.Deserialize<SyncRevision>(Json);
            if (revision is null || revision.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(revision.DeviceName) ||
                revision.DeviceName.Length > 80 || revision.SavedAt == default || revision.SavedAt > DateTimeOffset.MaxValue.AddDays(-1))
                throw new JsonException();
            node.Remove("sync");
            if (node["profile"] is JsonObject profile) profile.Remove("usdaApiKey");
            var backup = node.ToJsonString(Json);
            if (backups.Preview(backup).Kind != BackupKind.Current) throw new JsonException();
            return new RemoteSnapshot(backup, revision);
        }
        catch (Exception ex) when (ex is JsonException or BackupFormatException)
        {
            throw new SyncTargetException("Die Datei sync.json auf dem NAS enthält keinen gültigen PROJECT-MASS-Stand.");
        }
    }

    private async Task SaveConflictAsync(ISyncTarget target, string backup, string device)
    {
        var name = new string(device.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        if (name.Length == 0) name = "Geraet";
        var timestamp = clock.GetUtcNow().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await target.WriteAsync($"{BackupFolder}/konflikt-{timestamp}-{name}-{suffix}.json", backup);
    }

    private static async Task MaintainDailyBackupAsync(ISyncTarget target, string backup, DateOnly date)
    {
        var path = $"{BackupFolder}/{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";
        if (await target.ReadAsync(path) is null)
            await target.TryReplaceAsync(path, null, backup);
        var files = await target.ListAsync(BackupFolder);
        var old = files.Distinct(StringComparer.Ordinal)
            .Where(name => name.Length == 15 && name.EndsWith(".json", StringComparison.Ordinal) &&
                DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderDescending(StringComparer.Ordinal).Skip(14);
        foreach (var name in old) await target.DeleteAsync($"{BackupFolder}/{name}");
    }

    private async Task MarkBackupAsync(DateTimeOffset at)
    {
        await storage.WithLocalDataAsync(async () =>
        {
            if (storage.Inner is IndexedDbStorageService indexed)
                await indexed.MarkSyncBackupAsync(UserProfile.SingletonId, at);
            else
                await backups.MarkBackupAsync();
            return true;
        });
    }

    private sealed record LocalSnapshot(string Backup, DateTimeOffset? ChangedAt, bool HasDraft);
    private sealed record RemoteSnapshot(string Backup, SyncRevision Revision);
}
