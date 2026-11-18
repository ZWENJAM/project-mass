using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class WorkoutPlanService(IStorageService storage) : IWorkoutPlanService
{
    public async Task<List<WorkoutPlan>> GetAllAsync() =>
        (await storage.GetAllAsync<WorkoutPlan>(StorageStores.WorkoutPlans))
        .OrderBy(p => p.Day.HasValue ? ((int)p.Day.Value + 6) % 7 : 7).ThenBy(p => p.Name).ToList();

    public Task<WorkoutPlan?> GetAsync(Guid id) => storage.GetAsync<WorkoutPlan>(StorageStores.WorkoutPlans, id);
    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.WorkoutPlans, id);

    public async Task SaveAsync(WorkoutPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Trim().Length > 80)
            throw new ArgumentException("Bitte einen Plannamen mit 1–80 Zeichen angeben.");
        if (plan.Exercises.Count == 0 || plan.Exercises.Count > 50)
            throw new ArgumentException("Ein Plan benötigt 1–50 Übungen.");
        var exerciseIds = (await storage.GetAllAsync<Exercise>(StorageStores.Exercises)).Select(e => e.Id).ToHashSet();
        foreach (var e in plan.Exercises)
        {
            if (!exerciseIds.Contains(e.ExerciseId)) throw new ArgumentException("Eine Übung wurde nicht gefunden.");
            if (e.Sets is < 1 or > 20 || e.MinReps < 1 || e.MaxReps < e.MinReps || e.MaxReps > 999 || e.RestSeconds is < 0 or > 3600)
                throw new ArgumentException("Bitte gültige Sätze (1–20), Wiederholungen (1–999, von ≤ bis) und Pause (0–3600 s) angeben.");
        }
        plan.Name = plan.Name.Trim();
        await storage.PutAsync(StorageStores.WorkoutPlans, plan);
    }

    public void MoveExercise(WorkoutPlan plan, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= plan.Exercises.Count || target < 0 || target >= plan.Exercises.Count) return;
        (plan.Exercises[index], plan.Exercises[target]) = (plan.Exercises[target], plan.Exercises[index]);
    }
}
