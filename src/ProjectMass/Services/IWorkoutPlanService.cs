using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IWorkoutPlanService
{
    Task<List<WorkoutPlan>> GetAllAsync();
    Task<WorkoutPlan?> GetAsync(Guid id);
    Task SaveAsync(WorkoutPlan plan);
    Task DeleteAsync(Guid id);
    void MoveExercise(WorkoutPlan plan, int index, int direction);
}
