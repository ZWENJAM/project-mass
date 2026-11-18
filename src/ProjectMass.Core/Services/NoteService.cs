using ProjectMass.Models;

namespace ProjectMass.Services;

public interface INoteService
{
    Task<List<Note>> GetAllAsync();
    Task SaveAsync(Note note);
    Task DeleteAsync(Guid id);
}

public sealed class NoteService(IStorageService storage) : INoteService
{
    public async Task<List<Note>> GetAllAsync() =>
        (await storage.GetAllAsync<Note>(StorageStores.Notes)).OrderByDescending(n => n.Date).ThenBy(n => n.Id).ToList();

    public async Task SaveAsync(Note note)
    {
        var text = note.Text.Trim();
        if (text.Length == 0) throw new ArgumentException("Die Notiz ist leer.");
        if (text.Length > 10000) throw new ArgumentException("Eine Notiz darf höchstens 10.000 Zeichen haben.");
        note.Text = text;
        await storage.PutAsync(StorageStores.Notes, note);
    }

    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.Notes, id);
}
