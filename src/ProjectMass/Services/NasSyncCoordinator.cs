using Microsoft.JSInterop;

namespace ProjectMass.Services;

public sealed class NasSyncCoordinator(SyncService service, ChangeTrackingStorageService storage, NasBrowser browser, TimeProvider clock)
{
    private CancellationTokenSource? debounce;
    private DotNetObjectReference<NasSyncCoordinator>? receiver;
    private Task? initialization;
    private SyncRevision? dismissedConflict;
    private bool changedWhileBusy;

    public event Action? Changed;
    public NasSyncSettings Settings { get; private set; } = new();
    public NasSyncResult? Result { get; private set; }
    public bool Ready { get; private set; }
    public bool Busy { get; private set; }
    public bool FolderSupported { get; private set; }
    public bool NeedsFolderPermission { get; private set; }
    public bool ShowConflict { get; private set; }
    public bool ViewNeedsRefresh { get; private set; }
    public string? Error { get; private set; }
    public string? ConnectionMessage { get; private set; }
    public bool Enabled => Settings.Target.HasValue;

    public string Status => Error is not null ? "Fehler"
        : NeedsFolderPermission ? "Ausstehend"
        : Result?.Status == NasSyncStatus.Conflict ? "Konflikt"
        : Busy || Result?.Status is NasSyncStatus.Pending or NasSyncStatus.WaitingForWorkout || Settings.LastCompletedAt is null
            ? "Ausstehend" : "Synchron";

    public string? LastSyncLabel => Settings.LastCompletedAt is { } timestamp
        ? $"Zuletzt synchronisiert: {clock.LocalTimeZone.LocalLabel(timestamp, clock.GetLocalNow())}, {Settings.LastRemoteDeviceName ?? Settings.DeviceName}"
        : null;

    public Task StartAsync() => initialization ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            FolderSupported = await browser.IsFolderSupportedAsync();
            var settings = await service.GetSettingsAsync();
            if (settings is null)
            {
                settings = new NasSyncSettings { DeviceName = await browser.DefaultDeviceNameAsync() };
                await service.SaveSettingsAsync(settings);
            }
            Settings = settings;
            if (FolderSupported && Settings.Target == NasTargetKind.Folder) await browser.LoadFolderAsync();
            storage.Changed += LocalChanged;
            receiver = DotNetObjectReference.Create(this);
            await browser.CallAsync("watchVisibility", receiver);
            Ready = true;
            Changed?.Invoke();
            await SyncNowAsync();
        }
        catch (Exception) { SetError("NAS-Sync konnte nicht gestartet werden."); }
    }

    [JSInvokable]
    public Task BecameVisible() => SyncNowAsync();

    public async Task SyncNowAsync() => await RunSyncAsync(null);

    public async Task<bool> ChooseFolderAsync(string deviceName)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var name = NormalizeName(deviceName);
            var folder = await browser.ChooseFolderAsync();
            if (folder is null) return;
            await service.ResetTargetAsync(Settings with
            {
                Target = NasTargetKind.Folder, FolderName = folder, DeviceName = name,
                WebDavUrl = "", Username = "", Password = ""
            });
            await ReloadSettingsAsync();
            ResetResult();
            saved = true;
        });
        if (saved) await SyncNowAsync();
        return saved;
    }

    public async Task<bool> EnableWebDavAsync(string url, string username, string password, string deviceName)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var endpoint = ValidateWebDav(url, username);
            var name = NormalizeName(deviceName);
            var updated = Settings with
            {
                Target = NasTargetKind.WebDav, WebDavUrl = endpoint.AbsoluteUri,
                Username = username.Trim(), Password = password, DeviceName = name, FolderName = ""
            };
            var sameTarget = Settings.Target == NasTargetKind.WebDav && Settings.WebDavUrl == updated.WebDavUrl &&
                Settings.Username == updated.Username;
            if (sameTarget) await service.SaveSettingsAsync(updated);
            else await service.ResetTargetAsync(updated);
            if (Settings.Target == NasTargetKind.Folder) await browser.ForgetFolderAsync();
            await ReloadSettingsAsync();
            ResetResult();
            saved = true;
        });
        if (saved) await SyncNowAsync();
        return saved;
    }

    public Task SaveDeviceNameAsync(string deviceName) => RunAsync(async () =>
    {
        await service.SaveSettingsAsync(Settings with { DeviceName = NormalizeName(deviceName) });
        await ReloadSettingsAsync();
        ConnectionMessage = "Gerätename gespeichert.";
    });

    public async Task RenewPermissionAsync()
    {
        await RunAsync(async () => NeedsFolderPermission = !await browser.RequestFolderPermissionAsync());
        if (!NeedsFolderPermission) await SyncNowAsync();
    }

    public Task TestAsync(NasTargetKind kind, string url, string username, string password) => RunAsync(async () =>
    {
        var target = kind == NasTargetKind.Folder
            ? (ISyncTarget)new FolderSyncTarget(browser)
            : new WebDavSyncTarget(browser, ValidateWebDav(url, username), username.Trim(), password);
        await target.TestAsync();
        ConnectionMessage = "Verbindung funktioniert.";
    });

    public Task StopAsync() => RunAsync(async () =>
    {
        if (Settings.Target == NasTargetKind.Folder) await browser.ForgetFolderAsync();
        await service.ResetTargetAsync(Settings with
        {
            Target = null, FolderName = "", WebDavUrl = "", Username = "", Password = ""
        });
        await ReloadSettingsAsync();
        ResetResult();
        debounce?.Cancel();
        ConnectionMessage = "Sync beendet.";
    });

    public void OpenConflict()
    {
        if (Result?.Conflict is null) return;
        ShowConflict = true;
        Changed?.Invoke();
    }

    public void DismissConflict()
    {
        dismissedConflict = Result?.Conflict;
        ShowConflict = false;
        Changed?.Invoke();
    }

    public Task ResolveAsync(SyncResolution resolution) => Result?.Conflict is { } revision
        ? RunSyncAsync(new(revision, resolution)) : Task.CompletedTask;

    private async Task RunSyncAsync(ConflictChoice? choice)
    {
        if (!Ready || !Enabled) return;
        if (Busy) { changedWhileBusy = true; return; }
        await RunAsync(async () =>
        {
            ISyncTarget target;
            if (Settings.Target == NasTargetKind.Folder)
            {
                if (!FolderSupported || !await browser.HasFolderPermissionAsync())
                    throw new FolderPermissionException();
                NeedsFolderPermission = false;
                target = new FolderSyncTarget(browser);
            }
            else target = new WebDavSyncTarget(browser, WebDavSyncTarget.ValidateUrl(Settings.WebDavUrl),
                Settings.Username, Settings.Password);

            Result = await service.SyncAsync(target, choice);
            await ReloadSettingsAsync();
            ViewNeedsRefresh |= Result.Imported;
            Error = Result.Error;
            ShowConflict = Result.Conflict is { } conflict && conflict != dismissedConflict;
            if (Result.Status == NasSyncStatus.Pending && Result.Error is null) changedWhileBusy = true;
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        Error = null;
        ConnectionMessage = null;
        Changed?.Invoke();
        try { await action(); }
        catch (FolderPermissionException)
        {
            NeedsFolderPermission = true;
            Result = new(NasSyncStatus.Pending);
            ShowConflict = false;
        }
        catch (SyncTargetException ex) { Error = ex.Message; }
        catch (Exception) { Error = "Der NAS-Abgleich ist fehlgeschlagen. Prüfe die Verbindung und versuche es erneut."; }
        finally
        {
            Busy = false;
            Changed?.Invoke();
            if (changedWhileBusy)
            {
                changedWhileBusy = false;
                if (Enabled) Schedule();
            }
        }
    }

    private void LocalChanged()
    {
        if (!Ready || !Enabled) return;
        if (Result?.Status != NasSyncStatus.Conflict) Result = new(NasSyncStatus.Pending);
        Changed?.Invoke();
        Schedule();
    }

    private void Schedule()
    {
        debounce?.Cancel();
        var next = new CancellationTokenSource();
        debounce = next;
        _ = RunAfterDelayAsync(next);
    }

    private async Task RunAfterDelayAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), source.Token);
            await SyncNowAsync();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(debounce, source)) debounce = null;
            source.Dispose();
        }
    }

    private async Task ReloadSettingsAsync() => Settings = await service.GetSettingsAsync() ?? Settings;
    private void SetError(string message) { Error = message; Changed?.Invoke(); }

    private void ResetResult()
    {
        Result = null;
        ShowConflict = false;
        NeedsFolderPermission = false;
        dismissedConflict = null;
    }

    private static Uri ValidateWebDav(string url, string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Contains(':'))
            throw new SyncTargetException("Gib einen WebDAV-Benutzernamen ohne Doppelpunkt ein.");
        return WebDavSyncTarget.ValidateUrl(url);
    }

    private static string NormalizeName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 80) throw new SyncTargetException("Gib einen Gerätenamen mit bis zu 80 Zeichen ein.");
        return name;
    }
}

internal static class SyncTimeLabels
{
    public static string LocalLabel(this TimeZoneInfo timeZone, DateTimeOffset timestamp, DateTimeOffset localNow)
    {
        var local = TimeZoneInfo.ConvertTime(timestamp, timeZone);
        return local.Date == localNow.Date ? $"heute {local:HH:mm}" : $"{local:dd.MM.yyyy HH:mm}";
    }
}
