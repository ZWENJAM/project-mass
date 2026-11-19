using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class StatisticsServiceTests
{
    private readonly StatisticsService statistics = new();
    private readonly Guid exerciseId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private WorkoutSession Session(DateOnly date, decimal kg = 60, int reps = 12, Guid? plan = null) => new()
    {
        Date = date, IsCompleted = true, WorkoutPlanId = plan,
        Exercises = [new() { ExerciseId = exerciseId, Name = "Testübung", MaxReps = 12, Sets = [new() { WeightKg = kg, Reps = reps, IsCompleted = true }] }]
    };

    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(60, 12, 84)]
    [InlineData(60, 30, 120)]
    public void EpleyIncludesSingleRepException(decimal kg, int reps, decimal expected) => Assert.Equal(expected, statistics.Epley(kg, reps));

    [Fact]
    public void RecordsIgnoreDraftsWarmupsDropsAndUncompletedSetsAndKeepDates()
    {
        var first = Session(Monday.AddDays(-1), 80, 1);
        var later = Session(Monday, 60, 15); // e1RM 90, 15 reps; highest kg remains 80.
        later.Exercises[0].Sets.Add(new() { WeightKg = 999, Reps = 99, IsCompleted = false });
        later.Exercises[0].Sets.Add(new() { WeightKg = 999, Reps = 99, IsCompleted = true, Type = SetType.WarmUp });
        later.Exercises[0].Sets.Add(new() { WeightKg = 999, Reps = 99, IsCompleted = true, Type = SetType.Drop });
        var draft = Session(Monday.AddDays(1), 900, 90); draft.IsCompleted = false;
        var records = statistics.GetPersonalRecords([draft, later, first]);
        Assert.Equal(3, records.Count);
        var e1rm = records.Single(r => r.Kind == RecordKind.EstimatedOneRepMax);
        Assert.Equal(90, e1rm.Value); Assert.Equal(Monday, e1rm.Date);
        var weight = records.Single(r => r.Kind == RecordKind.Weight);
        Assert.Equal(80, weight.Value); Assert.Equal(first.Date, weight.Date);
        Assert.Equal(15, records.Single(r => r.Kind == RecordKind.Reps).Value);
        var newRecords = statistics.GetNewRecords(later, [first, later]);
        Assert.Equal(2, newRecords.Count);
        Assert.DoesNotContain(newRecords, r => r.Kind == RecordKind.Weight);
        var tie = Session(Monday.AddDays(1), 60, 15);
        Assert.Empty(statistics.GetNewRecords(tie, [first, later]));
    }

    [Fact]
    public void VolumeUsesMondaySundayAndCountsMultipleMusclesFully()
    {
        var exercise = new Exercise { Id = exerciseId, MuscleGroups = [MuscleGroup.Brust, MuscleGroup.Trizeps] };
        var sundayBefore = Session(Monday.AddDays(-1), 500, 10);
        var monday = Session(Monday, 60, 10);
        monday.Exercises[0].Sets.Add(new() { WeightKg = 100, Reps = 10, Type = SetType.WarmUp, IsCompleted = true });
        monday.Exercises[0].Sets.Add(new() { WeightKg = 100, Reps = 10, Type = SetType.Drop, IsCompleted = true });
        monday.Exercises[0].Sets.Add(new() { WeightKg = 100, Reps = 10, IsCompleted = false });
        var sunday = Session(Monday.AddDays(6), 50, 8);
        var nextMonday = Session(Monday.AddDays(7), 500, 10);
        var volume = statistics.GetMuscleVolume([sundayBefore, monday, sunday, nextMonday], [exercise], Monday.AddDays(3));
        Assert.Equal(2, volume.Count);
        Assert.All(volume, v => { Assert.Equal(1000, v.VolumeKg); Assert.Equal(2, v.WorkingSets); });
        Assert.Equal(Monday, statistics.WeekStart(Monday.AddDays(6)));
        Assert.Equal(Monday.AddDays(-7), statistics.WeekStart(sundayBefore.Date));
    }

    [Fact]
    public void MovingAverageUsesSevenCalendarDaysNotSevenMeasurements()
    {
        var points = new[] { new ChartPoint(Monday, 80), new ChartPoint(Monday.AddDays(2), 82), new ChartPoint(Monday.AddDays(6), 84), new ChartPoint(Monday.AddDays(8), 88), new ChartPoint(Monday.AddDays(20), 90) };
        var average = statistics.MovingAverage(points.Reverse());
        Assert.Equal(new[] { 80m, 81m, 82m, 254m / 3m, 90m }, average.Select(p => p.Value));
        Assert.Equal(points.Select(p => p.Date), average.Select(p => p.Date));
        Assert.Empty(statistics.MovingAverage([]));
    }

    [Fact]
    public void StreakIgnoresUnreachedCurrentWeekAndIncludesItOnlyOnceReached()
    {
        var today = Monday.AddDays(2);
        var sessions = new List<WorkoutSession>
        {
            Session(Monday.AddDays(-14)), Session(Monday.AddDays(-13)),
            Session(Monday.AddDays(-7)), Session(Monday.AddDays(-6)), Session(Monday)
        };
        Assert.Equal(2, statistics.GetStreak(sessions, 2, today));
        sessions.Add(Session(Monday.AddDays(1)));
        Assert.Equal(3, statistics.GetStreak(sessions, 2, today));
        sessions.Add(Session(Monday.AddDays(6))); // Future dates must not count.
        Assert.Equal(0, statistics.GetStreak(sessions, 0, today));
        Assert.Equal(0, statistics.GetStreak(sessions, 3, today));
        sessions.RemoveAll(s => s.Date == Monday.AddDays(-6));
        Assert.Equal(1, statistics.GetStreak(sessions, 2, today));
    }

    [Fact]
    public void ProgressionRequiresAllWorkingSetsAndUsesLatestExerciseSession()
    {
        var upper = new Exercise { Id = exerciseId, MuscleGroups = [MuscleGroup.Brust] };
        var profile = new UserProfile(); var session = Session(Monday);
        session.Exercises[0].Sets.Add(new() { WeightKg = 50, Reps = 12, IsCompleted = true });
        session.Exercises[0].Sets.Add(new() { Type = SetType.WarmUp, Reps = 1, IsCompleted = false });
        Assert.Equal(2.5m, statistics.GetProgression(exerciseId, [session], [upper], profile)!.IncreaseKg);
        session.Exercises[0].Sets[1].IsCompleted = false;
        Assert.Null(statistics.GetProgression(exerciseId, [session], [upper], profile));
        session.Exercises[0].Sets[1].IsCompleted = true; session.Exercises[0].Sets[1].Reps = 11;
        Assert.Null(statistics.GetProgression(exerciseId, [session], [upper], profile));
        session.Exercises[0].Sets[1].Reps = 12;
        var latest = Session(Monday.AddDays(1), 60, 10);
        Assert.Null(statistics.GetProgression(exerciseId, [session, latest], [upper], profile));
        session.Exercises[0].Sets.RemoveAll(s => s.Type == SetType.Working);
        Assert.Null(statistics.GetProgression(exerciseId, [session], [upper], profile));
    }

    [Theory]
    [InlineData(MuscleGroup.Beine)]
    [InlineData(MuscleGroup.Gesäß)]
    [InlineData(MuscleGroup.Waden)]
    public void LowerBodyProgressionUsesConfigurableStepAndRequiresExplicitApplication(MuscleGroup muscle)
    {
        var exercise = new Exercise { Id = exerciseId, MuscleGroups = [muscle] };
        var profile = new UserProfile(); var session = Session(Monday);
        Assert.Equal(5, statistics.GetProgression(exerciseId, [session], [exercise], profile)!.IncreaseKg);
        profile.LowerBodyIncreaseKg = 7.5m;
        var suggestion = statistics.GetProgression(exerciseId, [session], [exercise], profile)!;
        var draft = new SessionExercise { ExerciseId = exerciseId, Sets = [new() { WeightKg = 60 }, new() { WeightKg = 20, Type = SetType.WarmUp }] };
        Assert.Equal(60, draft.Sets[0].WeightKg); // Computing the suggestion does not change anything.
        statistics.ApplyProgression(draft, suggestion);
        Assert.Equal(67.5m, draft.Sets[0].WeightKg); Assert.Equal(20, draft.Sets[1].WeightKg);
        Assert.True(draft.ProgressionApplied);
        Assert.Throws<ArgumentException>(() => statistics.ApplyProgression(draft, suggestion));
    }

    [Fact]
    public void NextPlanPrefersUntrainedPlansThenOldestSessionThisWeek()
    {
        var plans = new[] { new WorkoutPlan { Name = "A" }, new WorkoutPlan { Name = "B" }, new WorkoutPlan { Name = "C" } };
        var history = new List<WorkoutSession> { Session(Monday, plan: plans[0].Id), Session(Monday.AddDays(-7), plan: plans[1].Id) };
        Assert.Equal(plans[2].Id, statistics.GetNextPlan(plans, history, Monday.AddDays(2))!.Id);
        history.Add(Session(Monday.AddDays(1), plan: plans[2].Id));
        Assert.Equal(plans[1].Id, statistics.GetNextPlan(plans, history, Monday.AddDays(2))!.Id);
        history.Add(Session(Monday.AddDays(2), plan: plans[1].Id));
        Assert.Equal(plans[0].Id, statistics.GetNextPlan(plans, history, Monday.AddDays(2))!.Id);
        Assert.Null(statistics.GetNextPlan([], history, Monday));
    }

    [Fact]
    public void SameDayOrderingUsesCompletionTimeForRecentRecordsAndNextPlan()
    {
        var a = new WorkoutPlan(); var b = new WorkoutPlan();
        var early = Session(Monday, plan: b.Id); early.CompletedAt = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        var late = Session(Monday, plan: a.Id); late.CompletedAt = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        late.Exercises[0].ExerciseId = Guid.NewGuid();
        Assert.Equal(b.Id, statistics.GetNextPlan([a, b], [early, late], Monday)!.Id);
        Assert.Equal(late.Id, statistics.GetPersonalRecords([early, late])[0].SessionId);
    }

    [Fact]
    public void NextPlanPrefersTodayThenUpcomingUntrainedPlanThenFlexibleRotation()
    {
        var today = Monday.AddDays(2);
        var flexible = new WorkoutPlan();
        var past = new WorkoutPlan { Day = DayOfWeek.Monday };
        var current = new WorkoutPlan { Day = DayOfWeek.Wednesday };
        var upcoming = new WorkoutPlan { Day = DayOfWeek.Friday };
        WorkoutPlan[] plans = [flexible, past, upcoming, current];
        var history = new List<WorkoutSession> { Session(Monday.AddDays(-2), plan: current.Id) };
        var draft = Session(today, plan: current.Id);
        draft.IsCompleted = false;
        history.Add(draft);
        Assert.Same(current, statistics.GetNextPlan(plans, history, today));
        history.Add(Session(today, plan: current.Id));
        Assert.Same(upcoming, statistics.GetNextPlan(plans, history, today));
        history.Add(Session(Monday, plan: upcoming.Id));
        Assert.Same(past, statistics.GetNextPlan(plans, history, today));
        history.Add(Session(Monday, plan: past.Id));
        Assert.Same(flexible, statistics.GetNextPlan(plans, history, today));
        Assert.Same(upcoming, statistics.GetNextPlan([current, upcoming], history, today));
    }

    [Fact]
    public void WeeklyTargetFallsBackToScheduledPlanCountWithoutEditingProfile()
    {
        WorkoutPlan[] plans = [new() { Day = DayOfWeek.Monday }, new(), new() { Day = DayOfWeek.Monday }];
        var profile = new UserProfile { TrainingDaysPerWeek = 0 };
        Assert.Equal(2, statistics.GetWeeklyTarget(profile, plans));
        Assert.Equal(0, profile.TrainingDaysPerWeek);
        Assert.Equal(0, statistics.GetWeeklyTarget(profile, [new()]));
        profile.TrainingDaysPerWeek = 4;
        Assert.Equal(4, statistics.GetWeeklyTarget(profile, plans));
    }

    [Fact]
    public void WeeklyReportAveragesOnlyLoggedDaysAndComparesWeekWeightsWithoutDuplicatingVolume()
    {
        FoodLogEntry Log(DateOnly date, decimal grams) => new() { Date = date, AmountGrams = grams, FoodSnapshot = new() { NutrientsPer100G = new() { Kcal = 100, ProteinGrams = 10 } } };
        var logs = new[] { Log(Monday, 100), Log(Monday, 200), Log(Monday.AddDays(2), 100), Log(Monday.AddDays(-1), 999) };
        var weights = new[] { new BodyMeasurement { Date = Monday.AddDays(-1), WeightKg = 82 }, new BodyMeasurement { Date = Monday.AddDays(-7), WeightKg = 84 }, new BodyMeasurement { Date = Monday, WeightKg = 80 }, new BodyMeasurement { Date = Monday.AddDays(2), WeightKg = 82 }, new BodyMeasurement { Date = Monday.AddDays(3), WaistCm = 90 } };
        var report = statistics.GetWeeklyReport(Monday.AddDays(6), logs, weights, [Session(Monday, 60, 10), Session(Monday.AddDays(7), 100, 10)]);
        Assert.Equal(2, report.LoggedDays); Assert.Equal(200, report.AverageKcal); Assert.Equal(20, report.AverageProtein);
        Assert.Equal(81, report.AverageWeight); Assert.Equal(83, report.PreviousAverageWeight); Assert.Equal(-2, report.WeightChange);
        Assert.Equal(1, report.Trainings); Assert.Equal(600, report.TotalVolumeKg);
        var empty = statistics.GetWeeklyReport(Monday.AddDays(28), logs, weights, []);
        Assert.Null(empty.AverageKcal); Assert.Null(empty.AverageProtein); Assert.Null(empty.WeightChange);
    }

    [Fact]
    public void FrequencyContainsTwelveMondaySundayWeeksAndIgnoresDraftsAndFuture()
    {
        var draft = Session(Monday); draft.IsCompleted = false;
        var days = statistics.GetFrequency([Session(Monday), Session(Monday), Session(Monday.AddDays(1)), draft], Monday);
        Assert.Equal(84, days.Count); Assert.Equal(DayOfWeek.Monday, days[0].Date.DayOfWeek); Assert.Equal(DayOfWeek.Sunday, days[^1].Date.DayOfWeek);
        Assert.Equal(2, days.Single(d => d.Date == Monday).Sessions);
        Assert.Equal(0, days.Single(d => d.Date == Monday.AddDays(1)).Sessions);
    }

    [Fact]
    public async Task MeasurementsRequireOneValueAndDriveProfileWeightAfterCreateEditAndDelete()
    {
        var storage = new InMemoryStorageService(); var measurements = new MeasurementService(storage);
        var profiles = new ProfileService(storage, new NutritionCalculator(), TimeProvider.System);
        var profile = new UserProfile { CurrentWeightKg = 90 };
        await Assert.ThrowsAsync<ArgumentException>(() => measurements.SaveAsync(new()));
        var m = new BodyMeasurement { Date = Monday, WeightKg = 80 }; await measurements.SaveAsync(m);
        Assert.Equal(80, await profiles.GetEffectiveWeightAsync(profile));
        m.WeightKg = 81; await measurements.SaveAsync(m);
        Assert.Equal(81, await profiles.GetEffectiveWeightAsync(profile));
        await measurements.DeleteAsync(m.Id); Assert.Equal(90, await profiles.GetEffectiveWeightAsync(profile));
    }

    [Fact]
    public async Task CompletingWorkoutPersistsNewPrBadgeWithSnapshot()
    {
        var storage = new InMemoryStorageService(); var plans = new WorkoutPlanService(storage);
        var sessions = new WorkoutSessionService(storage, plans, TimeProvider.System, statistics);
        var session = Session(Monday); session.IsCompleted = false;
        await sessions.SaveDraftAsync(session); await sessions.CompleteAsync(session);
        Assert.Equal(3, (await sessions.GetAsync(session.Id))!.NewPersonalRecords.Count);
        var tied = Session(Monday.AddDays(1)); tied.IsCompleted = false;
        await sessions.SaveDraftAsync(tied); await sessions.CompleteAsync(tied);
        Assert.Empty((await sessions.GetAsync(tied.Id))!.NewPersonalRecords);
    }
}
