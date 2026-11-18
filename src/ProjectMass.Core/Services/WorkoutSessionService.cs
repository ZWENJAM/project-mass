using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class WorkoutSessionService(IStorageService storage, IWorkoutPlanService plans, TimeProvider clock, IStatisticsService? statistics = null) : IWorkoutSessionService
{
    public async Task<WorkoutSession?> GetDraftAsync() =>
        (await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions))
        .Where(s => !s.IsCompleted).OrderByDescending(s => s.StartedAt).FirstOrDefault();

    public Task<WorkoutSession?> GetAsync(Guid id) => storage.GetAsync<WorkoutSession>(StorageStores.WorkoutSessions, id);

    public async Task<List<WorkoutSession>> GetHistoryAsync() =>
        (await storage.GetAllAsync<WorkoutSession>(StorageStores.WorkoutSessions))
        .Where(s => s.IsCompleted).OrderByDescending(s => s.Date).ThenByDescending(s => s.CompletedAt).ToList();

    public async Task<WorkoutSession> StartAsync(Guid planId)
    {
        // A single draft prevents accidentally losing or replacing an active workout.
        var draft = await GetDraftAsync();
        if (draft is not null) return draft;
        var plan = await plans.GetAsync(planId) ?? throw new ArgumentException("Trainingsplan nicht gefunden.");
        var catalog = (await storage.GetAllAsync<Exercise>(StorageStores.Exercises)).ToDictionary(e => e.Id);
        var history = await GetHistoryAsync();
        var session = new WorkoutSession
        {
            WorkoutPlanId = plan.Id, Name = plan.Name, StartedAt = clock.GetUtcNow(),
            Date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime)
        };
        foreach (var planned in plan.Exercises)
        {
            var last = history.SelectMany(s => s.Exercises).FirstOrDefault(e => e.ExerciseId == planned.ExerciseId);
            var exercise = new SessionExercise
            {
                ExerciseId = planned.ExerciseId,
                Name = catalog.TryGetValue(planned.ExerciseId, out var item) ? item.Name : "Unbekannte Übung",
                MinReps = planned.MinReps, MaxReps = planned.MaxReps, RestSeconds = planned.RestSeconds
            };
            for (var i = 0; i < planned.Sets; i++)
            {
                var previous = last?.Sets.ElementAtOrDefault(i);
                exercise.Sets.Add(new SetEntry
                {
                    WeightKg = previous?.WeightKg ?? 0, Reps = previous?.Reps ?? 0,
                    Type = previous?.Type ?? SetType.Working
                    // RPE and completion describe this workout and are deliberately reset.
                });
            }
            session.Exercises.Add(exercise);
        }
        await SaveDraftAsync(session);
        return session;
    }

    public async Task SaveDraftAsync(WorkoutSession session)
    {
        if (session.IsCompleted) throw new InvalidOperationException("Das Training ist bereits abgeschlossen.");
        Validate(session);
        await storage.PutAsync(StorageStores.WorkoutSessions, session);
    }

    public async Task CompleteAsync(WorkoutSession session)
    {
        Validate(session);
        if (session.IsCompleted) throw new InvalidOperationException("Das Training ist bereits abgeschlossen.");
        if (CountSets(session).Completed == 0) throw new ArgumentException("Bitte mindestens einen absolvierten Satz abhaken.");
        // Do not mutate the live draft unless the database commit succeeds.
        var completed = System.Text.Json.JsonSerializer.Deserialize<WorkoutSession>(System.Text.Json.JsonSerializer.Serialize(session))!;
        completed.IsCompleted = true;
        completed.CompletedAt = clock.GetUtcNow();
        completed.RestEndsAt = null;
        completed.NewPersonalRecords = (statistics ?? new StatisticsService()).GetNewRecords(completed, await GetHistoryAsync());
        await storage.PutAsync(StorageStores.WorkoutSessions, completed);
        session.IsCompleted = true;
        session.CompletedAt = completed.CompletedAt;
        session.RestEndsAt = null;
        session.NewPersonalRecords = completed.NewPersonalRecords;
    }

    public Task DeleteAsync(Guid id) => storage.DeleteAsync(StorageStores.WorkoutSessions, id);

    public async Task ToggleSetAsync(WorkoutSession session, SessionExercise exercise, SetEntry set)
    {
        if (!session.Exercises.Contains(exercise) || !exercise.Sets.Contains(set)) throw new ArgumentException("Satz gehört nicht zu diesem Training.");
        if (!set.IsCompleted && set.Reps < 1) throw new ArgumentException("Bitte zuerst Wiederholungen eintragen.");
        var wasCompleted = set.IsCompleted;
        var previousEnd = session.RestEndsAt;
        set.IsCompleted = !wasCompleted;
        if (set.IsCompleted) session.RestEndsAt = clock.GetUtcNow().AddSeconds(exercise.RestSeconds);
        try { await SaveDraftAsync(session); }
        catch { set.IsCompleted = wasCompleted; session.RestEndsAt = previousEnd; throw; }
    }

    public void AddSet(SessionExercise exercise)
    {
        if (exercise.Sets.Count >= 30) throw new ArgumentException("Maximal 30 Sätze pro Übung.");
        var last = exercise.Sets.LastOrDefault();
        exercise.Sets.Add(new SetEntry { WeightKg = last?.WeightKg ?? 0, Reps = last?.Reps ?? 0, Type = last?.Type ?? SetType.Working });
    }

    public async Task StartRestAsync(WorkoutSession session, SessionExercise exercise)
    {
        if (!session.Exercises.Contains(exercise)) throw new ArgumentException("Übung gehört nicht zu diesem Training.");
        var previous = session.RestEndsAt;
        session.RestEndsAt = clock.GetUtcNow().AddSeconds(exercise.RestSeconds);
        try { await SaveDraftAsync(session); }
        catch { session.RestEndsAt = previous; throw; }
    }

    public async Task StopRestAsync(WorkoutSession session)
    {
        var previous = session.RestEndsAt;
        session.RestEndsAt = null;
        try { await SaveDraftAsync(session); }
        catch { session.RestEndsAt = previous; throw; }
    }

    public async Task ExtendRestAsync(WorkoutSession session)
    {
        var previous = session.RestEndsAt;
        var now = clock.GetUtcNow();
        session.RestEndsAt = (previous > now ? previous.Value : now).AddSeconds(30);
        try { await SaveDraftAsync(session); }
        catch { session.RestEndsAt = previous; throw; }
    }

    public void RemoveSet(SessionExercise exercise, SetEntry set)
    {
        if (exercise.Sets.Count <= 1) throw new ArgumentException("Mindestens ein Satz muss erhalten bleiben.");
        exercise.Sets.Remove(set);
    }

    public int GetRemainingRestSeconds(WorkoutSession session) => session.RestEndsAt is { } end
        ? (int)Math.Max(0, Math.Ceiling((end - clock.GetUtcNow()).TotalSeconds)) : 0;

    public (int Completed, int Total) CountSets(WorkoutSession session) =>
        (session.Exercises.Sum(e => e.Sets.Count(s => s.IsCompleted)), session.Exercises.Sum(e => e.Sets.Count));

    private static void Validate(WorkoutSession session)
    {
        foreach (var set in session.Exercises.SelectMany(e => e.Sets))
            if (set.WeightKg is < 0 or > 2000 || set.Reps is < 0 or > 999 || set.Rpe is < 1 or > 10 || (set.IsCompleted && set.Reps == 0))
                throw new ArgumentException("Bitte gültige Werte angeben: Gewicht 0–2000 kg, Wiederholungen 0–999 (abgehakt mindestens 1), RPE 1–10 oder leer.");
    }
}
