using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class WorkoutSessionServiceTests
{
    private readonly InMemoryStorageService storage = new();
    private readonly ManualClock clock = new();
    private WorkoutSessionService Service => new(storage, new WorkoutPlanService(storage), clock);

    private async Task<(WorkoutPlan Plan, Exercise Exercise)> SeedPlan(int sets = 3)
    {
        var exercise = new Exercise { Name = "Brustpresse", MuscleGroups = [MuscleGroup.Brust] };
        await storage.PutAsync(StorageStores.Exercises, exercise);
        var plan = new WorkoutPlan { Name = "Mein Plan", Exercises = [new PlannedExercise { ExerciseId = exercise.Id, Sets = sets, RestSeconds = 90 }] };
        await new WorkoutPlanService(storage).SaveAsync(plan);
        return (plan, exercise);
    }

    [Fact]
    public async Task PrefillsLatestCompletedSessionByExerciseIdAcrossPlansAndRenames()
    {
        var (plan, exercise) = await SeedPlan();
        var older = new WorkoutSession
        {
            Date = new(2026, 11, 1), IsCompleted = true,
            Exercises = [new SessionExercise { ExerciseId = exercise.Id, Name = "Brustpresse", Sets = [new SetEntry { WeightKg = 20, Reps = 8 }] }]
        };
        var latest = new WorkoutSession
        {
            Date = new(2026, 11, 15), IsCompleted = true, WorkoutPlanId = Guid.NewGuid(),
            Exercises = [new SessionExercise { ExerciseId = exercise.Id, Name = "Alter Name", Sets = [new SetEntry { WeightKg = 45, Reps = 12, Rpe = 9, IsCompleted = true, Type = SetType.WarmUp }, new SetEntry { WeightKg = 60, Reps = 10, IsCompleted = true }] }]
        };
        // Similar names with different IDs must not supply the previous values.
        var other = new WorkoutSession
        {
            Date = new(2026, 11, 16), IsCompleted = true,
            Exercises = [new SessionExercise { ExerciseId = Guid.NewGuid(), Name = exercise.Name, Sets = [new SetEntry { WeightKg = 999, Reps = 1 }] }]
        };
        foreach (var s in new[] { other, latest, older }) await storage.PutAsync(StorageStores.WorkoutSessions, s);

        var draft = await Service.StartAsync(plan.Id);
        var current = Assert.Single(draft.Exercises);
        Assert.Equal(exercise.Name, current.Name);
        Assert.Equal(3, current.Sets.Count);
        Assert.Equal(45, current.Sets[0].WeightKg);
        Assert.Equal(12, current.Sets[0].Reps);
        Assert.Equal(SetType.WarmUp, current.Sets[0].Type);
        Assert.Equal(60, current.Sets[1].WeightKg);
        Assert.Equal(0, current.Sets[2].WeightKg);
        Assert.All(current.Sets, s => { Assert.False(s.IsCompleted); Assert.Null(s.Rpe); });
        Assert.NotEqual(latest.Exercises[0].Sets[0].Id, current.Sets[0].Id);
        current.Sets[0].WeightKg = 1;
        var unchanged = await Service.GetAsync(latest.Id);
        Assert.Equal(45, unchanged!.Exercises[0].Sets[0].WeightKg);
    }

    [Fact]
    public async Task ExistingDraftIsResumedAndPersistsAcrossServiceInstances()
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        draft.Note = "Technik beachten";
        draft.Exercises[0].Sets[0].WeightKg = 42.5m;
        draft.Exercises[0].Sets[0].Rpe = 7.5m;
        await Service.SaveDraftAsync(draft);
        var resumed = await Service.StartAsync(Guid.NewGuid());
        Assert.Equal(draft.Id, resumed.Id);
        Assert.Equal(draft.Note, resumed.Note);
        Assert.Equal(42.5m, resumed.Exercises[0].Sets[0].WeightKg);
        Assert.Equal(7.5m, resumed.Exercises[0].Sets[0].Rpe);
        Assert.Empty(await Service.GetHistoryAsync());
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(1000, 89)]
    [InlineData(89500, 1)]
    [InlineData(90000, 0)]
    [InlineData(300000, 0)]
    public async Task RestTimerUsesPersistedDeadlineAndCeiling(int elapsedMilliseconds, int expected)
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        var e = draft.Exercises[0];
        e.Sets[0].Reps = 10;
        await Service.ToggleSetAsync(draft, e, e.Sets[0]);
        Assert.Equal(clock.GetUtcNow().AddSeconds(90), draft.RestEndsAt);
        clock.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        var restored = await Service.GetDraftAsync();
        Assert.Equal(expected, Service.GetRemainingRestSeconds(restored!));
    }

    [Fact]
    public async Task UncheckingDoesNotRestartTimerAndStopPersists()
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        var e = draft.Exercises[0]; var set = e.Sets[0]; set.Reps = 8;
        await Service.ToggleSetAsync(draft, e, set);
        var deadline = draft.RestEndsAt;
        clock.Advance(TimeSpan.FromSeconds(10));
        await Service.ToggleSetAsync(draft, e, set);
        Assert.Equal(deadline, draft.RestEndsAt);
        await Service.StopRestAsync(draft);
        Assert.Null((await Service.GetDraftAsync())!.RestEndsAt);
        Assert.Equal(0, Service.GetRemainingRestSeconds(draft));
    }

    [Fact]
    public void CountsAllSetTypesAcrossExercisesAndUpdatesAfterAddRemove()
    {
        var session = new WorkoutSession
        {
            Exercises = [new SessionExercise { Sets = [new SetEntry { Type = SetType.WarmUp, IsCompleted = true }, new SetEntry { Type = SetType.Working }, new SetEntry { Type = SetType.Drop, IsCompleted = true }] }, new SessionExercise { Sets = [new SetEntry { IsCompleted = true }] }]
        };
        Assert.Equal((3, 4), Service.CountSets(session));
        Service.AddSet(session.Exercises[0]);
        Assert.Equal((3, 5), Service.CountSets(session));
        Assert.False(session.Exercises[0].Sets[^1].IsCompleted);
        Service.RemoveSet(session.Exercises[0], session.Exercises[0].Sets[0]);
        Assert.Equal((2, 4), Service.CountSets(session));
        Assert.Throws<ArgumentException>(() => Service.RemoveSet(session.Exercises[1], session.Exercises[1].Sets[0]));
        Assert.Equal((0, 0), Service.CountSets(new()));
    }

    [Fact]
    public async Task CompleteMovesDraftToHistoryAndClearsRestDeadline()
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.CompleteAsync(draft));
        var e = draft.Exercises[0]; e.Sets[0].Reps = 8;
        await Service.ToggleSetAsync(draft, e, e.Sets[0]);
        await Service.CompleteAsync(draft);
        Assert.True(draft.IsCompleted);
        Assert.Null(draft.RestEndsAt);
        Assert.Null(await Service.GetDraftAsync());
        var complete = Assert.Single(await Service.GetHistoryAsync());
        Assert.Equal(clock.GetUtcNow(), complete.CompletedAt);
        await Service.DeleteAsync(complete.Id);
        Assert.Empty(await Service.GetHistoryAsync());
    }

    [Fact]
    public async Task WriteFailureDoesNotPretendToggleOrCompletionSucceeded()
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        var e = draft.Exercises[0]; var set = e.Sets[0]; set.Reps = 8;
        storage.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ToggleSetAsync(draft, e, set));
        Assert.False(set.IsCompleted); Assert.Null(draft.RestEndsAt);
        storage.FailWrites = false;
        await Service.ToggleSetAsync(draft, e, set);
        storage.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.CompleteAsync(draft));
        Assert.False(draft.IsCompleted);
        Assert.NotNull(draft.RestEndsAt);
        Assert.NotNull(await Service.GetDraftAsync());
    }

    [Theory]
    [InlineData(-1, 10, null)]
    [InlineData(20, -1, null)]
    [InlineData(20, 10, 11)]
    [InlineData(20, 10, 0)]
    public async Task InvalidSetValuesAreRejected(int kg, int reps, int? rpe)
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        var set = draft.Exercises[0].Sets[0]; set.WeightKg = kg; set.Reps = reps; set.Rpe = rpe;
        await Assert.ThrowsAsync<ArgumentException>(() => Service.SaveDraftAsync(draft));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    [Fact]
    public async Task ExtendRestAddsThirtySecondsToDeadlineAndPersistsAcrossReload()
    {
        var (plan, _) = await SeedPlan();
        var draft = await Service.StartAsync(plan.Id);
        await Service.ExtendRestAsync(draft);
        Assert.Equal(clock.GetUtcNow().AddSeconds(30), draft.RestEndsAt);
        clock.Advance(TimeSpan.FromSeconds(10));
        await Service.ExtendRestAsync(draft);
        Assert.Equal(50, Service.GetRemainingRestSeconds((await Service.GetDraftAsync())!));
        storage.FailWrites = true;
        var previous = draft.RestEndsAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ExtendRestAsync(draft));
        Assert.Equal(previous, draft.RestEndsAt);
        storage.FailWrites = false;
        clock.Advance(TimeSpan.FromMinutes(2));
        await Service.ExtendRestAsync(draft);
        Assert.Equal(30, Service.GetRemainingRestSeconds(draft));
    }
}
