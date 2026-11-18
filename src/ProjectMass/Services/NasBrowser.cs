using Microsoft.JSInterop;

namespace ProjectMass.Services;

public sealed class NasBrowser(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/nasSync.js").AsTask());

    internal async Task<T> CallAsync<T>(string method, params object?[] args) =>
        await (await module.Value).InvokeAsync<T>(method, args);

    internal async Task CallAsync(string method, params object?[] args) =>
        await (await module.Value).InvokeVoidAsync(method, args);

    public Task<bool> IsFolderSupportedAsync() => CallAsync<bool>("isFolderSupported");
    public Task<string> DefaultDeviceNameAsync() => CallAsync<string>("defaultDeviceName");
    public Task<string?> LoadFolderAsync() => CallAsync<string?>("loadFolder");
    public Task<string?> ChooseFolderAsync() => CallAsync<string?>("chooseFolder");
    public Task<bool> HasFolderPermissionAsync() => CallAsync<bool>("hasFolderPermission");
    public Task<bool> RequestFolderPermissionAsync() => CallAsync<bool>("requestFolderPermission");
    public Task ForgetFolderAsync() => CallAsync("forgetFolder");
}

public sealed class FolderSyncTarget(NasBrowser browser) : ISyncTarget
{
    public async Task<string?> ReadAsync(string path) => (await RequestAsync("read", path)).Content;

    public async Task WriteAsync(string path, string content) => await RequestAsync("write", path, content);

    public async Task<IReadOnlyList<string>> ListAsync(string folder) =>
        (await RequestAsync("list", folder)).Files ?? [];

    public async Task DeleteAsync(string path) => await RequestAsync("delete", path);

    public async Task<bool> TryReplaceAsync(string path, string? expected, string content) =>
        (await RequestAsync("write", path, content, true, expected)).Matched;

    public Task TestAsync() => SyncTargetProbe.TestAsync(this);

    private async Task<FolderReply> RequestAsync(string method, string path, string? content = null,
        bool conditional = false, string? expected = null)
    {
        var reply = await browser.CallAsync<FolderReply>("folderRequest", method, path, content, conditional, expected);
        if (!reply.Permission) throw new FolderPermissionException();
        if (reply.Failed) throw new SyncTargetException("Der NAS-Ordner ist nicht erreichbar oder nicht beschreibbar.");
        return reply;
    }

    private sealed record FolderReply(bool Permission, bool Failed, bool Matched, string? Content, List<string>? Files);
}

internal static class SyncTargetProbe
{
    internal static async Task TestAsync(ISyncTarget target)
    {
        const string folder = "project-mass";
        var name = $"test-{Guid.NewGuid():N}.json";
        var path = $"{folder}/{name}";
        await target.WriteAsync(path, "{}");
        try
        {
            if (await target.ReadAsync(path) != "{}" || !(await target.ListAsync(folder)).Contains(name))
                throw new SyncTargetException("Die Testdatei konnte nicht gelesen werden.");
        }
        finally { await target.DeleteAsync(path); }
    }
}
