using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class SearchPolicyTests
{
    [Fact]
    public async Task DebounceWaitsFiveHundredMillisecondsAndNewInputCancelsPreviousDelay()
    {
        var clock = new SearchClock(); var policy = new SearchRequestPolicy(clock);
        using var cancellation = new CancellationTokenSource();
        var old = policy.DebounceAsync(cancellation.Token);
        clock.Advance(TimeSpan.FromMilliseconds(300)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        var current = policy.DebounceAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(499)); Assert.False(current.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1)); await current;
        Assert.False(SearchRequestPolicy.CanSearch(" ab "));
        Assert.True(SearchRequestPolicy.CanSearch(" abc "));
        Assert.Equal("skyr natur", SearchRequestPolicy.Normalize("  SKYR  Natur "));
    }

    [Fact]
    public void RateLimitIsTenRequestsPerRollingMinute()
    {
        var clock = new SearchClock(); var policy = new SearchRequestPolicy(clock);
        for (var i = 0; i < 10; i++) policy.ReserveOffSearch();
        Assert.Throws<FoodSearchException>(() => policy.ReserveOffSearch());
        clock.Advance(TimeSpan.FromMilliseconds(59999));
        Assert.Throws<FoodSearchException>(() => policy.ReserveOffSearch());
        clock.Advance(TimeSpan.FromMilliseconds(1)); policy.ReserveOffSearch();
    }

    [Fact]
    public async Task CacheNormalizesKeysSeparatesProvidersAndExpiresAtSevenDays()
    {
        var storage = new InMemoryStorageService(); var clock = new SearchClock();
        var cache = new SearchCacheService(storage, clock);
        await cache.PutAsync(FoodSource.OpenFoodFacts, "  SKYR Natur ", [new() { Name = "Skyr" }]);
        Assert.Single((await cache.GetAsync(FoodSource.OpenFoodFacts, "skyr   natur"))!);
        Assert.Null(await cache.GetAsync(FoodSource.Usda, "skyr natur"));
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMilliseconds(1));
        Assert.NotNull(await cache.GetAsync(FoodSource.OpenFoodFacts, "skyr natur"));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await cache.GetAsync(FoodSource.OpenFoodFacts, "skyr natur"));
    }

    [Fact]
    public async Task OfflineUsesCacheOrOwnFoodsWithoutAnyHttpAndCancelledInputNeverRequests()
    {
        var storage = new InMemoryStorageService(); var clock = new SearchClock();
        var foods = new FoodService(storage); var cache = new SearchCacheService(storage, clock);
        var profiles = new ProfileService(storage, new NutritionCalculator(), clock);
        var policy = new SearchRequestPolicy(clock);
        var handler = new FoodApiTests.FixtureHandler("off-search.json");
        using var http = new HttpClient(handler) { BaseAddress = new("https://world.openfoodfacts.org/") };
        var service = new FoodSearchService(new(http, policy), new(http, profiles), policy, cache, new Offline(), foods, profiles);
        await cache.PutAsync(FoodSource.OpenFoodFacts, "Skyr", [new() { Name = "Cache-Skyr" }]);
        var hitTask = service.SearchAsync(FoodSource.OpenFoodFacts, "SKYR", CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(500)); var hit = await hitTask;
        Assert.Equal("Cache-Skyr", Assert.Single(hit.Foods).Name); Assert.Contains("Offline", hit.Message);
        await foods.SaveAsync(new() { Name = "Eigener Quark" });
        var missTask = service.SearchAsync(FoodSource.OpenFoodFacts, "Quark", CancellationToken.None);
        clock.Advance(TimeSpan.FromMilliseconds(500)); var miss = await missTask;
        Assert.Equal("Eigener Quark", Assert.Single(miss.Foods).Name); Assert.Contains("offline", miss.Message);
        using var cancellation = new CancellationTokenSource();
        var cancelled = service.SearchAsync(FoodSource.OpenFoodFacts, "Yogurt", cancellation.Token);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Empty(handler.Urls);
    }
    private sealed class Offline : IBrowserConnectivity { public Task<bool> IsOnlineAsync() => Task.FromResult(false); }
}

internal sealed class SearchClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly List<ManualTimer> timers = [];
    public override DateTimeOffset GetUtcNow() => now;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    public void Advance(TimeSpan duration)
    {
        now += duration;
        foreach (var timer in timers.ToArray()) timer.FireIfDue();
    }
    private sealed class ManualTimer(SearchClock clock, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? due;
        private TimeSpan period;
        public bool Change(TimeSpan dueTime, TimeSpan interval) { due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime; period = interval; return true; }
        public void FireIfDue() { if (due is not { } time || time > clock.now) return; due = period > TimeSpan.Zero ? clock.now + period : null; callback(state); }
        public void Dispose() { due = null; clock.timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
