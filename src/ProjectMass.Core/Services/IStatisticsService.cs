using ProjectMass.Models;

namespace ProjectMass.Services;

public interface IStatisticsService
{
    decimal Epley(decimal kg, int reps);
    DateOnly WeekStart(DateOnly date);
    List<PersonalRecord> GetPersonalRecords(IEnumerable<WorkoutSession> sessions);
    List<PersonalRecord> GetNewRecords(WorkoutSession session, IEnumerable<WorkoutSession> history);
    List<MuscleVolume> GetMuscleVolume(IEnumerable<WorkoutSession> sessions, IEnumerable<Exercise> exercises, DateOnly week);
    List<FrequencyDay> GetFrequency(IEnumerable<WorkoutSession> sessions, DateOnly today);
    List<ChartPoint> MovingAverage(IEnumerable<ChartPoint> points);
    int GetStreak(IEnumerable<WorkoutSession> sessions, int weeklyTarget, DateOnly today);
    int GetWeeklyTarget(UserProfile profile, IEnumerable<WorkoutPlan> plans);
    ProgressionSuggestion? GetProgression(Guid exerciseId, IEnumerable<WorkoutSession> history, IEnumerable<Exercise> exercises, UserProfile profile);
    void ApplyProgression(SessionExercise exercise, ProgressionSuggestion suggestion);
    WorkoutPlan? GetNextPlan(IEnumerable<WorkoutPlan> plans, IEnumerable<WorkoutSession> sessions, DateOnly today);
    WeeklyReport GetWeeklyReport(DateOnly week, IEnumerable<FoodLogEntry> logs, IEnumerable<BodyMeasurement> measurements, IEnumerable<WorkoutSession> sessions);
}
