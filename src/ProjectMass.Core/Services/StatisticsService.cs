using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class StatisticsService : IStatisticsService
{
    public decimal Epley(decimal kg, int reps)
    {
        if (kg < 0 || reps < 1) throw new ArgumentException("Gewicht muss mindestens 0 und die Wiederholungszahl positiv sein.");
        return reps == 1 ? kg : kg * (1m + reps / 30m);
    }
    public DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    private static IEnumerable<(WorkoutSession Session, SessionExercise Exercise, SetEntry Set)> Working(IEnumerable<WorkoutSession> sessions) =>
        sessions.Where(s => s.IsCompleted).SelectMany(s => s.Exercises.SelectMany(e => e.Sets
            .Where(t => t.Type == SetType.Working && t.IsCompleted && t.Reps > 0 && t.WeightKg >= 0).Select(t => (s, e, t))));
    public List<PersonalRecord> GetPersonalRecords(IEnumerable<WorkoutSession> sessions)
    {
        var source = sessions.ToList();
        var moments = source.ToDictionary(s => s.Id, s => s.CompletedAt ?? s.StartedAt);
        var records = new Dictionary<(Guid, RecordKind), PersonalRecord>();
        foreach (var row in Working(source).OrderBy(r => r.Session.Date).ThenBy(r => r.Session.CompletedAt).ThenBy(r => r.Session.Id))
            foreach (var (kind, value) in new[] { (RecordKind.EstimatedOneRepMax, Epley(row.Set.WeightKg, row.Set.Reps)), (RecordKind.Weight, row.Set.WeightKg), (RecordKind.Reps, (decimal)row.Set.Reps) })
            {
                var key = (row.Exercise.ExerciseId, kind);
                if (!records.TryGetValue(key, out var previous) || value > previous.Value)
                    records[key] = new(row.Exercise.ExerciseId, row.Exercise.Name, kind, value, row.Session.Date, row.Session.Id);
            }
        return records.Values.OrderByDescending(r => r.Date).ThenByDescending(r => moments[r.SessionId]).ThenBy(r => r.ExerciseName).ThenBy(r => r.Kind).ToList();
    }
    public List<PersonalRecord> GetNewRecords(WorkoutSession session, IEnumerable<WorkoutSession> history)
    {
        var previous = GetPersonalRecords(history.Where(s => s.Id != session.Id)).ToDictionary(r => (r.ExerciseId, r.Kind));
        return GetPersonalRecords([session]).Where(r => !previous.TryGetValue((r.ExerciseId, r.Kind), out var old) || r.Value > old.Value).ToList();
    }
    public List<MuscleVolume> GetMuscleVolume(IEnumerable<WorkoutSession> sessions, IEnumerable<Exercise> exercises, DateOnly week)
    {
        var start = WeekStart(week); var end = start.AddDays(7);
        var catalog = exercises.ToDictionary(e => e.Id);
        var totals = new Dictionary<MuscleGroup, (decimal Kg, int Sets)>();
        foreach (var row in Working(sessions.Where(s => s.Date >= start && s.Date < end)))
        {
            if (!catalog.TryGetValue(row.Exercise.ExerciseId, out var exercise)) continue;
            foreach (var muscle in exercise.MuscleGroups.Distinct())
            {
                var old = totals.GetValueOrDefault(muscle);
                totals[muscle] = (old.Kg + row.Set.WeightKg * row.Set.Reps, old.Sets + 1);
            }
        }
        return totals.OrderBy(t => t.Key).Select(t => new MuscleVolume(t.Key, t.Value.Kg, t.Value.Sets)).ToList();
    }
    public List<FrequencyDay> GetFrequency(IEnumerable<WorkoutSession> sessions, DateOnly today)
    {
        var start = WeekStart(today).AddDays(-77);
        var counts = sessions.Where(s => s.IsCompleted && s.Date <= today).GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.Count());
        return Enumerable.Range(0, 84).Select(i => new FrequencyDay(start.AddDays(i), counts.GetValueOrDefault(start.AddDays(i)))).ToList();
    }
    public List<ChartPoint> MovingAverage(IEnumerable<ChartPoint> points)
    {
        var values = points.OrderBy(p => p.Date).ToList();
        return values.Select(p => new ChartPoint(p.Date, values.Where(v => v.Date >= p.Date.AddDays(-6) && v.Date <= p.Date).Average(v => v.Value))).ToList();
    }
    public int GetStreak(IEnumerable<WorkoutSession> sessions, int weeklyTarget, DateOnly today)
    {
        if (weeklyTarget <= 0) return 0;
        var counts = sessions.Where(s => s.IsCompleted && s.Date <= today).GroupBy(s => WeekStart(s.Date)).ToDictionary(g => g.Key, g => g.Count());
        var week = WeekStart(today);
        // An unfinished current week neither adds to nor breaks the streak.
        if (counts.GetValueOrDefault(week) < weeklyTarget) week = week.AddDays(-7);
        var streak = 0;
        while (counts.GetValueOrDefault(week) >= weeklyTarget) { streak++; week = week.AddDays(-7); }
        return streak;
    }
    public ProgressionSuggestion? GetProgression(Guid exerciseId, IEnumerable<WorkoutSession> history, IEnumerable<Exercise> exercises, UserProfile profile)
    {
        var last = history.Where(s => s.IsCompleted).OrderByDescending(s => s.Date).ThenByDescending(s => s.CompletedAt)
            .SelectMany(s => s.Exercises).FirstOrDefault(e => e.ExerciseId == exerciseId);
        var work = last?.Sets.Where(s => s.Type == SetType.Working).ToList();
        if (work is null || work.Count == 0 || work.Any(s => !s.IsCompleted || s.Reps < last!.MaxReps)) return null;
        var exercise = exercises.FirstOrDefault(e => e.Id == exerciseId);
        if (exercise is null || exercise.Type != ExerciseType.Strength) return null;
        var lower = exercise.MuscleGroups.Any(g => g is MuscleGroup.Beine or MuscleGroup.Gesäß or MuscleGroup.Waden);
        var increase = lower ? profile.LowerBodyIncreaseKg : profile.UpperBodyIncreaseKg;
        return increase > 0 ? new(exerciseId, increase) : null;
    }
    public void ApplyProgression(SessionExercise exercise, ProgressionSuggestion suggestion)
    {
        if (exercise.ProgressionApplied || exercise.ExerciseId != suggestion.ExerciseId || suggestion.IncreaseKg <= 0) throw new ArgumentException("Dieser Vorschlag kann nicht übernommen werden.");
        var sets = exercise.Sets.Where(s => s.Type == SetType.Working && !s.IsCompleted).ToList();
        if (exercise.Sets.Any(s => s.Type == SetType.Working && s.IsCompleted) || sets.Count == 0) throw new ArgumentException("Bitte den Vorschlag vor dem ersten Arbeitssatz übernehmen.");
        if (sets.Any(s => s.WeightKg + suggestion.IncreaseKg > 2000)) throw new ArgumentException("Das neue Gewicht ist zu hoch.");
        foreach (var set in sets) set.WeightKg += suggestion.IncreaseKg;
        exercise.ProgressionApplied = true;
    }
    public WorkoutPlan? GetNextPlan(IEnumerable<WorkoutPlan> plans, IEnumerable<WorkoutSession> sessions, DateOnly today)
    {
        var start = WeekStart(today);
        var lastByPlan = sessions.Where(s => s.IsCompleted && s.Date <= today && s.WorkoutPlanId.HasValue)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.CompletedAt ?? s.StartedAt)
            .DistinctBy(s => s.WorkoutPlanId).ToDictionary(s => s.WorkoutPlanId!.Value);
        var entries = plans.Select(plan => (Plan: plan, Last: lastByPlan.GetValueOrDefault(plan.Id))).ToList();
        var scheduled = entries.Where(p => p.Plan.Day.HasValue && (p.Last is null || p.Last.Date < start))
            .OrderBy(p => ((int)p.Plan.Day!.Value - (int)today.DayOfWeek + 7) % 7)
            .Select(p => p.Plan).FirstOrDefault();
        if (scheduled is not null) return scheduled;

        var flexible = entries.Where(p => !p.Plan.Day.HasValue).ToList();
        return (flexible.Count > 0 ? flexible : entries)
            .OrderBy(p => p.Last?.Date >= start).ThenBy(p => p.Last?.Date)
            .ThenBy(p => p.Last?.CompletedAt ?? p.Last?.StartedAt).Select(p => p.Plan).FirstOrDefault();
    }

    public int GetWeeklyTarget(UserProfile profile, IEnumerable<WorkoutPlan> plans) =>
        profile.TrainingDaysPerWeek > 0 ? profile.TrainingDaysPerWeek : plans.Count(plan => plan.Day.HasValue);
    public WeeklyReport GetWeeklyReport(DateOnly week, IEnumerable<FoodLogEntry> logs, IEnumerable<BodyMeasurement> measurements, IEnumerable<WorkoutSession> sessions)
    {
        var start = WeekStart(week); var end = start.AddDays(7); var previous = start.AddDays(-7);
        var dayTotals = logs.Where(e => e.Date >= start && e.Date < end).GroupBy(e => e.Date)
            .Select(g => new { Kcal = g.Sum(e => e.FoodSnapshot.NutrientsPer100G.Kcal * e.AmountGrams / 100m), Protein = g.Sum(e => e.FoodSnapshot.NutrientsPer100G.ProteinGrams * e.AmountGrams / 100m) }).ToList();
        var weights = measurements.Where(m => m.WeightKg.HasValue).ToList();
        var currentWeight = weights.Where(m => m.Date >= start && m.Date < end).Select(m => m.WeightKg!.Value).ToList();
        var previousWeight = weights.Where(m => m.Date >= previous && m.Date < start).Select(m => m.WeightKg!.Value).ToList();
        decimal? currentAverage = currentWeight.Count > 0 ? currentWeight.Average() : null;
        decimal? previousAverage = previousWeight.Count > 0 ? previousWeight.Average() : null;
        var workouts = sessions.Where(s => s.IsCompleted && s.Date >= start && s.Date < end).ToList();
        return new(start, dayTotals.Count, dayTotals.Count > 0 ? dayTotals.Average(d => d.Kcal) : null,
            dayTotals.Count > 0 ? dayTotals.Average(d => d.Protein) : null, currentAverage, previousAverage,
            currentAverage - previousAverage, workouts.Count, Working(workouts).Sum(r => r.Set.WeightKg * r.Set.Reps));
    }
}
