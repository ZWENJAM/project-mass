using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public sealed class NasSyncTests
{
    private static readonly Guid OtherId = Guid.Parse("66000000-0000-0000-0000-000000000001");
    private readonly Clock clock = new();
    private readonly InMemoryStorageService raw = new();
    private readonly MemoryTarget target = new();
    private readonly ChangeTrackingStorageService storage;
    private readonly SyncService sync;

    public NasSyncTests()
    {
        storage = new(raw, clock);
        sync = new(storage, clock);
    }

    [Fact]
    public async Task FirstUploadUsesBackupFormatAndKeepsCredentialsAndKeysLocal()
    {
        var settings = await Configure();
        const string localKey = "lokaler-testwert";
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Alex", UsdaApiKey = localKey });
        Assert.Equal(NasSyncStatus.Synced, (await sync.SyncAsync(target)).Status);

        var payload = JsonNode.Parse(target.Files[SyncService.CurrentPath])!;
        Assert.Equal("project-mass", payload["format"]!.GetValue<string>());
        Assert.Equal("Alex", payload["profile"]!["name"]!.GetValue<string>());
        Assert.Equal(settings.DeviceId, payload["sync"]!["deviceId"]!.GetValue<Guid>());
        Assert.Equal("PC", payload["sync"]!["deviceName"]!.GetValue<string>());
        Assert.DoesNotContain("usdaApiKey", target.Files[SyncService.CurrentPath]);
        Assert.DoesNotContain("WebDavUrl", target.Files[SyncService.CurrentPath], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(clock.GetUtcNow(), (await sync.GetSettingsAsync())!.LastCompletedAt);
        Assert.Equal(clock.GetUtcNow(), (await LocalProfile()).LastBackupAt);
        var exported = await new BackupService(storage, clock).ExportJsonAsync();
        Assert.DoesNotContain("\"sync\"", exported);
        Assert.DoesNotContain("deviceId", exported);
        Assert.Contains(localKey, exported);
    }

    [Fact]
    public async Task RemoteOnlyChangeReplacesLocalDataWithoutTrackingTheImport()
    {
        await Configure();
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Alex", UsdaApiKey = "lokal" });
        var oldNote = new Note { Text = "Alt" };
        await storage.PutAsync(StorageStores.Notes, oldNote);
        await sync.SyncAsync(target);
        clock.Advance();
        PutRemote("Nas-Profil");
        var revision = target.Files[SyncService.CurrentPath];
        var stamp = (await raw.GetAsync<LocalSyncChange>(StorageStores.SyncMetadata, LocalSyncChange.Key))!.At;

        var result = await sync.SyncAsync(target);

        Assert.True(result.Imported);
        Assert.Equal("Nas-Profil", (await LocalProfile()).Name);
        Assert.Equal("lokal", (await LocalProfile()).UsdaApiKey);
        Assert.Null(await raw.GetAsync<Note>(StorageStores.Notes, oldNote.Id));
        Assert.Equal(stamp, (await raw.GetAsync<LocalSyncChange>(StorageStores.SyncMetadata, LocalSyncChange.Key))!.At);
        Assert.Equal(revision, target.Files[SyncService.CurrentPath]);
        Assert.False((await sync.SyncAsync(target)).Imported);
    }

    [Fact]
    public async Task LocalOnlyChangeUploadsAndOwnRemoteRevisionIsNotImported()
    {
        var settings = await Configure();
        await sync.SyncAsync(target);
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Lokal" });

        await sync.SyncAsync(target);

        Assert.Equal("Lokal", JsonNode.Parse(target.Files[SyncService.CurrentPath])!["profile"]!["name"]!.GetValue<string>());
        clock.Advance();
        PutRemote("Ferner eigener Stand", settings.DeviceId);
        var result = await sync.SyncAsync(target);
        Assert.False(result.Imported);
        Assert.Equal("Lokal", (await LocalProfile()).Name);
    }

    [Theory]
    [InlineData(SyncResolution.TakeNas, "NAS", "Lokal")]
    [InlineData(SyncResolution.UploadLocal, "Lokal", "NAS")]
    public async Task ConflictRequiresChoiceAndSavesOverwrittenData(SyncResolution resolution, string expectedName, string discardedName)
    {
        await Configure();
        await sync.SyncAsync(target);
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Lokal" });
        clock.Advance();
        PutRemote("NAS");
        var original = target.Files[SyncService.CurrentPath];
        var conflict = await sync.SyncAsync(target);

        Assert.Equal(NasSyncStatus.Conflict, conflict.Status);
        Assert.Equal("Lokal", (await LocalProfile()).Name);
        Assert.Equal(original, target.Files[SyncService.CurrentPath]);
        Assert.DoesNotContain(target.Files.Keys, name => name.Contains("konflikt-", StringComparison.Ordinal));

        var result = await sync.SyncAsync(target, new(conflict.Conflict!, resolution));

        Assert.Equal(NasSyncStatus.Synced, result.Status);
        Assert.Equal(expectedName, (await LocalProfile()).Name);
        var saved = Assert.Single(target.Files, file => file.Key.Contains("konflikt-", StringComparison.Ordinal)).Value;
        Assert.Equal(discardedName, JsonNode.Parse(saved)!["profile"]!["name"]!.GetValue<string>());
        Assert.Null(JsonNode.Parse(saved)!["sync"]);
    }

    [Fact]
    public async Task ChangedRemoteRevisionInvalidatesOldConflictChoice()
    {
        await Configure();
        await sync.SyncAsync(target);
        await storage.PutAsync(StorageStores.UserProfiles, new UserProfile { Name = "Lokal" });
        clock.Advance();
        PutRemote("NAS");
        var choice = new ConflictChoice((await sync.SyncAsync(target)).Conflict!, SyncResolution.UploadLocal);
        clock.Advance();
        PutRemote("Neuer NAS-Stand");

        var result = await sync.SyncAsync(target, choice);

        Assert.Equal(NasSyncStatus.Conflict, result.Status);
        Assert.NotEqual(choice.Revision, result.Conflict);
        Assert.Contains("Neuer NAS-Stand", target.Files[SyncService.CurrentPath]);
    }

    [Fact]
    public async Task DailyBackupIsIdempotentAndOnlyTheLast14DatesAreKept()
    {
        await Configure();
        for (var day = 1; day <= 17; day++)
            target.Files[$"{SyncService.BackupFolder}/2026-11-{day:00}.json"] = "{}";
        target.Files[$"{SyncService.BackupFolder}/konflikt-alt.json"] = "{}";

        await sync.SyncAsync(target);
        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Spätere Änderung" });
        await sync.SyncAsync(target);

        var daily = target.Files.Where(file => Path.GetFileName(file.Key).StartsWith("2026-11-", StringComparison.Ordinal)).ToList();
        Assert.Equal(14, daily.Count);
        Assert.DoesNotContain(daily, file => file.Key.EndsWith("2026-11-04.json", StringComparison.Ordinal));
        Assert.Empty(JsonNode.Parse(target.Files[$"{SyncService.BackupFolder}/2026-11-18.json"])!["notes"]!.AsArray());
        Assert.True(target.Files.ContainsKey($"{SyncService.BackupFolder}/konflikt-alt.json"));
    }

    [Fact]
    public async Task OpenWorkoutDefersImportButAllowsLocalUpload()
    {
        await Configure();
        var draft = new WorkoutSession { Name = "Offenes Training" };
        await storage.PutAsync(StorageStores.WorkoutSessions, draft);
        Assert.Equal(NasSyncStatus.Synced, (await sync.SyncAsync(target)).Status);
        Assert.Contains("Offenes Training", target.Files[SyncService.CurrentPath]);
        clock.Advance();
        PutRemote("NAS");

        Assert.Equal(NasSyncStatus.WaitingForWorkout, (await sync.SyncAsync(target)).Status);
        Assert.NotNull(await storage.GetAsync<WorkoutSession>(StorageStores.WorkoutSessions, draft.Id));

        draft.IsCompleted = true;
        await storage.PutAsync(StorageStores.WorkoutSessions, draft);
        Assert.Equal(NasSyncStatus.Conflict, (await sync.SyncAsync(target)).Status);
    }

    [Fact]
    public async Task EditsDuringUploadRemainPending()
    {
        await Configure();
        await storage.PutAsync(StorageStores.Notes, new Note { Text = "Erster Stand" });
        target.BeforeWrite = async path =>
        {
            if (path != SyncService.CurrentPath) return;
            target.BeforeWrite = null;
            await storage.PutAsync(StorageStores.Notes, new Note { Text = "Später Stand" });
        };

        Assert.Equal(NasSyncStatus.Pending, (await sync.SyncAsync(target)).Status);
        Assert.Single(JsonNode.Parse(target.Files[SyncService.CurrentPath])!["notes"]!.AsArray());
        Assert.Equal(NasSyncStatus.Synced, (await sync.SyncAsync(target)).Status);
        Assert.Contains(JsonNode.Parse(target.Files[SyncService.CurrentPath])!["notes"]!.AsArray(),
            note => note!["text"]!.GetValue<string>() == "Später Stand");
    }

    [Fact]
    public async Task PutDeleteAndClearTrackUserStoresButNotMetadataOrTheCache()
    {
        var note = new Note();
        var events = 0;
        storage.Changed += () => events++;
        await Configure();
        await storage.ClearAsync(StorageStores.SearchCache);
        await storage.ClearAsync(StorageStores.AppMetadata);
        Assert.Equal(0, events);
        await storage.PutAsync(StorageStores.Notes, note);
        await storage.DeleteAsync(StorageStores.Notes, note.Id);
        await storage.ClearAsync(StorageStores.Notes);
        Assert.Equal(3, events);
        var stamp = (await raw.GetAsync<LocalSyncChange>(StorageStores.SyncMetadata, LocalSyncChange.Key))!.At;
        Assert.Equal(clock.GetUtcNow().AddMilliseconds(2), stamp);
    }

    private async Task<NasSyncSettings> Configure()
    {
        var settings = new NasSyncSettings { Target = NasTargetKind.Folder };
        await sync.SaveSettingsAsync(settings);
        return settings;
    }

    private async Task<UserProfile> LocalProfile() =>
        (await storage.GetAsync<UserProfile>(StorageStores.UserProfiles, UserProfile.SingletonId))!;

    private void PutRemote(string name, Guid? deviceId = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.SerializeToNode(new BackupData { Profile = new UserProfile { Name = name } }, options)!;
        json["sync"] = JsonSerializer.SerializeToNode(new SyncRevision(deviceId ?? OtherId, "Handy", clock.GetUtcNow()), options);
        target.Files[SyncService.CurrentPath] = json.ToJsonString(options);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance() => now = now.AddMinutes(1);
    }

    private sealed class MemoryTarget : ISyncTarget
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public Func<string, Task>? BeforeWrite { get; set; }
        public Task<string?> ReadAsync(string path) => Task.FromResult(Files.GetValueOrDefault(path));
        public async Task WriteAsync(string path, string content)
        {
            if (BeforeWrite is not null) await BeforeWrite(path);
            Files[path] = content;
        }
        public Task<IReadOnlyList<string>> ListAsync(string folder) => Task.FromResult<IReadOnlyList<string>>(
            Files.Keys.Where(path => path.StartsWith(folder + '/', StringComparison.Ordinal)).Select(Path.GetFileName).OfType<string>().ToArray());
        public Task DeleteAsync(string path) { Files.Remove(path); return Task.CompletedTask; }
        public Task TestAsync() => Task.CompletedTask;
    }
}
