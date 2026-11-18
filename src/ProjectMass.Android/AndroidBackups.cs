using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Mobile;

internal sealed class BackupLifecycle
{
    public event Action? Paused;
    public void Pause() => Paused?.Invoke();
}

internal sealed class AndroidBackups(ChangeTrackingStorageService storage, IBackupService backups, NasSyncCoordinator nas,
    BackupFiles files, SafDocuments documents, BackupLifecycle lifecycle, TimeProvider clock) : INativeBackups, IDisposable
{
    private Func<Func<Task>, Task>? dispatch;
    private readonly SemaphoreSlim writer = new(1, 1);
    private bool scheduled, dirty, hasData, disposed, choosing;
    private string? recovery;
    private NasSyncResult? lastImport;
    public string? FolderName { get; private set; }
    public string? Status { get; private set; }
    public BackupPreview? RecoveryPreview { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync(Func<Func<Task>, Task> dispatch)
    {
        if (this.dispatch is not null) return;
        this.dispatch = dispatch;
        FolderName = documents.FolderName;
        hasData = await HasLocalDataAsync();
        if (!hasData) await LoadRecoveryAsync();
        storage.Changed += LocalChanged;
        lifecycle.Paused += Pause;
        nas.Changed += NasChanged;
        if (hasData) LocalChanged();
    }

    private async Task<bool> HasLocalDataAsync() =>
        await storage.GetAsync<LocalSyncChange>(StorageStores.SyncMetadata, LocalSyncChange.Key) is not null ||
        (await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions)).Count > 0 ||
        (await storage.GetAllAsync<FoodLogEntry>(StorageStores.FoodLogEntries)).Count > 0 ||
        (await storage.GetAllAsync<BodyMeasurement>(StorageStores.BodyMeasurements)).Count > 0 ||
        (await storage.GetAllAsync<Note>(StorageStores.Notes)).Count > 0 ||
        (await storage.GetAllAsync<WaterLog>(StorageStores.WaterLogs)).Count > 0;

    public async Task ChooseFolderAsync()
    {
        choosing = true;
        try
        {
            if (!await documents.ChooseFolderAsync()) return;
            FolderName = documents.FolderName;
            Status = null;
            if (!hasData) await LoadRecoveryAsync();
        }
        catch (Exception) { Status = "Der Sicherungsordner konnte nicht geöffnet werden. Wähle ihn erneut."; }
        finally { choosing = false; Changed?.Invoke(); }
        if (hasData && Status is null) await FlushAsync(force: true);
    }

    public async Task RestoreAsync()
    {
        var json = recovery;
        if (json is null) return;
        try
        {
            if (await HasLocalDataAsync())
            {
                DismissRecovery();
                Status = "Es gibt bereits lokale Daten. Nutze „Backup einspielen“, um sie zu ersetzen.";
                Changed?.Invoke();
                return;
            }
            await backups.ImportJsonAsync(json);
            DismissRecovery();
        }
        catch (Exception)
        {
            Status = "Die Sicherung konnte nicht wiederhergestellt werden.";
            Changed?.Invoke();
        }
    }

    public void DismissRecovery()
    {
        recovery = null;
        RecoveryPreview = null;
        Status = null;
        Changed?.Invoke();
    }

    private async Task LoadRecoveryAsync()
    {
        var candidates = new List<Func<Stream>>();
        try { candidates.AddRange(files.InternalCandidates().Select(path => (Func<Stream>)(() => File.OpenRead(path)))); }
        catch (IOException) { }
        try { candidates.AddRange(files.ExternalCandidates().Select(uri => (Func<Stream>)(() => documents.OpenRead(uri)))); }
        catch (Exception) { Status = "Der gewählte Sicherungsordner konnte nicht gelesen werden."; }
        foreach (var open in candidates)
        {
            try
            {
                await using var stream = open();
                var json = await BackupFiles.ReadAsync(stream, BackupFiles.MaximumBytes);
                var preview = backups.Preview(json);
                if (preview.Kind != BackupKind.Current ||
                    (RecoveryPreview is not null && preview.ExportedAt <= RecoveryPreview.ExportedAt)) continue;
                RecoveryPreview = preview;
                recovery = json;
            }
            catch (Exception ex) when (ex is IOException or BackupFormatException or Java.IO.IOException or Java.Lang.SecurityException) { }
        }
        Changed?.Invoke();
    }

    private void NasChanged()
    {
        if (nas.Result is not { Imported: true } result || ReferenceEquals(lastImport, result)) return;
        lastImport = result;
        LocalChanged();
    }

    private void LocalChanged()
    {
        hasData = true;
        dirty = true;
        if (!scheduled)
        {
            scheduled = true;
            _ = ScheduleAsync();
        }
    }

    private async Task ScheduleAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (disposed) return;
        await DispatchAsync(async () =>
        {
            scheduled = false;
            await FlushAsync(force: false);
        });
    }

    private void Pause() => _ = DispatchAsync(() => FlushAsync(force: true));

    private async Task DispatchAsync(Func<Task> work)
    {
        if (disposed || dispatch is null) return;
        try { await dispatch(work); }
        catch (ObjectDisposedException) { }
    }

    private async Task FlushAsync(bool force)
    {
        if (!hasData || disposed || choosing || RecoveryPreview is not null || (!force && !dirty)) return;
        await writer.WaitAsync();
        try
        {
            if (!force && !dirty) return;
            dirty = false;
            var json = await backups.ExportJsonAsync();
            var date = DateOnly.FromDateTime(clock.GetLocalNow().Date);
            await files.WriteInternalAsync(json, date);
            try
            {
                await files.WriteExternalAsync(json, date);
                Status = $"Automatisch gesichert: {clock.GetLocalNow():HH:mm}";
            }
            catch (Exception)
            {
                Status = "Im App-Speicher gesichert. Prüfe den Zugriff auf den Sicherungsordner.";
            }
        }
        catch (Exception)
        {
            dirty = true;
            Status = "Die automatische Sicherung konnte nicht geschrieben werden.";
        }
        finally
        {
            writer.Release();
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        disposed = true;
        storage.Changed -= LocalChanged;
        nas.Changed -= NasChanged;
        lifecycle.Paused -= Pause;
    }
}
