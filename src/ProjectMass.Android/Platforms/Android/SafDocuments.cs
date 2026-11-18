using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;
using Uri = Android.Net.Uri;

namespace ProjectMass.Mobile;

internal sealed class SafDocuments
{
    private const int RequestCode = 24719;
    private const string TreeKey = "backup-tree";
    private const ActivityFlags Access = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
    private TaskCompletionSource<Intent?>? pending;

    private static ContentResolver Resolver => global::Android.App.Application.Context.ContentResolver
        ?? throw new InvalidOperationException("Dateispeicher nicht verfügbar.");

    public Uri? Folder
    {
        get
        {
            var stored = Preferences.Default.Get(TreeKey, "");
            if (stored.Length == 0) return null;
            var uri = Uri.Parse(stored);
            return Resolver.PersistedUriPermissions?.Any(permission =>
                permission.Uri?.Equals(uri) == true && permission.IsReadPermission && permission.IsWritePermission) == true
                ? uri : null;
        }
    }

    public string? FolderName => Folder is { } tree ? DocumentsContract.GetTreeDocumentId(tree)?.Split('/').Last() : null;

    public async Task<bool> ChooseFolderAsync()
    {
        using var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(Access | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        using var result = await LaunchAsync(intent);
        if (result?.Data is not { } tree) return false;
        var access = result.Flags & Access;
        if (access != Access) throw new IOException("Der gewählte Ordner erlaubt keine Lese- und Schreibzugriffe.");
        var previous = Folder;
        Resolver.TakePersistableUriPermission(tree, access);
        Preferences.Default.Set(TreeKey, tree.ToString()!);
        if (previous is not null && !previous.Equals(tree))
        {
            try { Resolver.ReleasePersistableUriPermission(previous, Access); }
            catch (Java.Lang.SecurityException) { }
        }
        return true;
    }

    public async Task<bool> SaveDocumentAsync(string fileName, string mimeType, string content)
    {
        using var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType.Split(';')[0]);
        intent.PutExtra(Intent.ExtraTitle, fileName);
        using var result = await LaunchAsync(intent);
        if (result?.Data is not { } uri) return false;
        await WriteAsync(uri, content);
        return true;
    }

    public IReadOnlyDictionary<string, Uri> List(Uri folder)
    {
        var folderId = DocumentsContract.GetTreeDocumentId(folder) ?? throw new IOException("Ungültiger Sicherungsordner.");
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(folder, folderId);
        var files = new Dictionary<string, Uri>(StringComparer.Ordinal);
        using var cursor = Resolver.Query(children!, [DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnDocumentId],
            null, null, null) ?? throw new IOException("Sicherungsordner konnte nicht gelesen werden.");
        while (cursor.MoveToNext())
        {
            var name = cursor.GetString(0);
            var id = cursor.GetString(1);
            if (name is not null && id is not null && DocumentsContract.BuildDocumentUriUsingTree(folder, id) is { } file)
                files.TryAdd(name, file);
        }
        return files;
    }

    public async Task UpsertAsync(Uri folder, IReadOnlyDictionary<string, Uri> files, string name, string content)
    {
        if (!files.TryGetValue(name, out var uri))
        {
            var parent = DocumentsContract.BuildDocumentUriUsingTree(folder, DocumentsContract.GetTreeDocumentId(folder)!);
            uri = DocumentsContract.CreateDocument(Resolver, parent!, "application/json", name)
                ?? throw new IOException("Sicherungsdatei konnte nicht erstellt werden.");
        }
        await WriteAsync(uri, content);
    }

    public Stream OpenRead(Uri uri) => Resolver.OpenInputStream(uri) ?? throw new IOException("Sicherung nicht erreichbar.");

    public void Delete(Uri uri) => DocumentsContract.DeleteDocument(Resolver, uri);

    private static async Task WriteAsync(Uri uri, string content)
    {
        await using var stream = Resolver.OpenOutputStream(uri, "wt") ?? throw new IOException("Datei kann nicht geschrieben werden.");
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private Task<Intent?> LaunchAsync(Intent intent) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (pending is not null) throw new InvalidOperationException("Ein Dateidialog ist bereits geöffnet.");
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Kein aktives App-Fenster.");
        var completion = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        try
        {
#pragma warning disable CS0618, CA1422 // SAF is available from the app's minimum Android version.
            activity.StartActivityForResult(intent, RequestCode);
#pragma warning restore CS0618, CA1422
            return await completion.Task;
        }
        finally { pending = null; }
    });

    public void Complete(int requestCode, Result result, Intent? data)
    {
        if (requestCode == RequestCode) pending?.TrySetResult(result == Result.Ok ? data : null);
    }
}
