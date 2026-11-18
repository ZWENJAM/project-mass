using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class ExerciseService(IStorageService storage) : IExerciseService
{
    public async Task<List<Exercise>> GetAllAsync() =>
        (await storage.GetAllAsync<Exercise>(StorageStores.Exercises)).OrderBy(e => e.Name).ToList();

    public async Task<List<Exercise>> SearchAsync(string? query, MuscleGroup? muscleGroup) =>
        (await GetAllAsync()).Where(e =>
            (string.IsNullOrWhiteSpace(query) || e.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
            (!muscleGroup.HasValue || e.MuscleGroups.Contains(muscleGroup.Value))).ToList();

    public async Task<Exercise> UpdateAsync(Guid id, string name, List<MuscleGroup> muscleGroups, string equipment,
        List<string> steps, List<string> tips, List<string> mistakes, string? videoUrl)
    {
        name = name.Trim();
        if (name.Length is 0 or > 80) throw new ArgumentException("Bitte einen Namen mit bis zu 80 Zeichen eingeben.");
        var validVideo = ExerciseGuidance.HttpsVideoUrl(videoUrl);
        if (!string.IsNullOrWhiteSpace(videoUrl) && validVideo is null)
            throw new ArgumentException("Bitte einen vollständigen HTTPS-Videolink eingeben.");

        var exercise = await storage.GetAsync<Exercise>(StorageStores.Exercises, id) ??
            throw new ArgumentException("Übung nicht gefunden.");
        exercise.Name = name;
        exercise.MuscleGroups = muscleGroups;
        exercise.Equipment = equipment.Trim();
        exercise.Steps = steps;
        exercise.Tips = tips;
        exercise.Mistakes = mistakes;
        exercise.VideoUrl = validVideo;
        await storage.PutAsync(StorageStores.Exercises, exercise);
        return exercise;
    }
}
