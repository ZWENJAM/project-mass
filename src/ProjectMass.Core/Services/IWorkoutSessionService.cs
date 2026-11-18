using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IWorkoutSessionService
{
    Task<WorkoutSession?> GetDraftAsync();
    Task<WorkoutSession> StartAsync(Guid planId);
    Task<WorkoutSession?> GetAsync(Guid id);
    Task<List<WorkoutSession>> GetHistoryAsync();
    Task SaveDraftAsync(WorkoutSession session);
    Task CompleteAsync(WorkoutSession session);
    Task DeleteAsync(Guid id);
    Task ToggleSetAsync(WorkoutSession session, SessionExercise exercise, SetEntry set);
    Task StartRestAsync(WorkoutSession session, SessionExercise exercise);
    Task StopRestAsync(WorkoutSession session);
    Task ExtendRestAsync(WorkoutSession session);
    void AddSet(SessionExercise exercise);
    void RemoveSet(SessionExercise exercise, SetEntry set);
    int GetRemainingRestSeconds(WorkoutSession session);
    (int Completed, int Total) CountSets(WorkoutSession session);
}
