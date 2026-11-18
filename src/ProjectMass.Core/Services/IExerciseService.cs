using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IExerciseService
{
    Task<List<Exercise>> GetAllAsync();
    Task<List<Exercise>> SearchAsync(string? query, MuscleGroup? muscleGroup);
    Task<Exercise> UpdateAsync(Guid id, string name, List<MuscleGroup> muscleGroups, string equipment,
        List<string> steps, List<string> tips, List<string> mistakes, string? videoUrl);
}
