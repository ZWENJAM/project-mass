using ProjectMass.Services;

namespace ProjectMass.Mobile;

internal sealed class AndroidPlatformFiles(SafDocuments documents) : IPlatformFiles
{
    public Task<bool> SaveAsync(string fileName, string content, string mimeType) =>
        documents.SaveDocumentAsync(fileName, mimeType, content);

    public async Task<string> ShareAsync(string fileName, string content, string title, string text)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "sharing-root", "project-mass");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(fileName));
        await File.WriteAllTextAsync(path, content);
        await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title, File = new ShareFile(path, "text/plain")
        }));
        return "opened";
    }

    public Task OpenMailAsync(string subject, string body) => MainThread.InvokeOnMainThreadAsync(() =>
        Email.Default.ComposeAsync(new EmailMessage { Subject = subject, Body = body }));

    public async Task<string?> PickBackupAsync(long maxBytes)
    {
        var file = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "PROJECT-MASS-Backup auswählen",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/json", "text/plain", "application/octet-stream"]
            })
        }));
        if (file is null) return null;
        await using var stream = await file.OpenReadAsync();
        return await BackupFiles.ReadAsync(stream, maxBytes);
    }
}
