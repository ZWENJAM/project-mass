using Microsoft.JSInterop;

namespace ProjectMass.Services;

public interface IPlatformFiles
{
    Task<bool> SaveAsync(string fileName, string content, string mimeType);
    Task<string> ShareAsync(string fileName, string content, string title, string text);
    Task OpenMailAsync(string subject, string body);
    Task<string?> PickBackupAsync(long maxBytes) => Task.FromResult<string?>(null);
}

public sealed class BrowserPlatformFiles(IJSRuntime js) : IPlatformFiles, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/files.js").AsTask());

    public async Task<bool> SaveAsync(string fileName, string content, string mimeType)
    {
        await (await module.Value).InvokeVoidAsync("downloadText", fileName, content, mimeType);
        return true;
    }
    public async Task<string> ShareAsync(string fileName, string content, string title, string text) =>
        await (await module.Value).InvokeAsync<string>("shareTextFile", fileName, content, title, text);
    public async Task OpenMailAsync(string subject, string body) =>
        await (await module.Value).InvokeVoidAsync("openMail", subject, body);
    public async ValueTask DisposeAsync()
    {
        if (!module.IsValueCreated) return;
        try { await (await module.Value).DisposeAsync(); } catch (JSDisconnectedException) { }
    }
}

public interface INativeBackups
{
    string? FolderName { get; }
    string? Status { get; }
    BackupPreview? RecoveryPreview { get; }
    event Action? Changed;
    Task InitializeAsync(Func<Func<Task>, Task> dispatch);
    Task ChooseFolderAsync();
    Task RestoreAsync();
    void DismissRecovery();
}

public sealed record WebDavResponse(int Status, string? Content, string? Etag);

public interface IWebDavTransport
{
    string? NetworkFailureMessage => null;
    Task<WebDavResponse> RequestAsync(Uri uri, string method, string username, string password,
        string? content, IReadOnlyDictionary<string, string> headers);
}

public sealed class BrowserWebDavTransport(NasBrowser browser) : IWebDavTransport
{
    public Task<WebDavResponse> RequestAsync(Uri uri, string method, string username, string password,
        string? content, IReadOnlyDictionary<string, string> headers) =>
        browser.CallAsync<WebDavResponse>("webDavRequest", uri.AbsoluteUri, method, username, password, content, headers);
}
