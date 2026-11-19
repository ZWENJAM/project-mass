using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProjectMass.Services;

public sealed record ProjectMassOptions(bool Native = false, string DisplayVersion = AppInfo.Version);

public static class ProjectMassRegistration
{
    public static IServiceCollection AddProjectMassCore(this IServiceCollection services, Func<IServiceProvider, HttpClient> seedClient, ProjectMassOptions? platform = null)
    {
        var options = platform ?? new ProjectMassOptions();
        services.AddSingleton(options);
        services.AddScoped(seedClient);
        services.AddScoped<IndexedDbStorageService>();
        services.AddScoped(sp => new ChangeTrackingStorageService(
            sp.GetRequiredService<IndexedDbStorageService>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IStorageService>(sp => sp.GetRequiredService<ChangeTrackingStorageService>());
        services.AddScoped<NasBrowser>();
        services.TryAddScoped<IWebDavTransport, BrowserWebDavTransport>();
        services.TryAddScoped<IPlatformFiles, BrowserPlatformFiles>();
        services.AddScoped<SyncService>();
        services.AddScoped<NasSyncCoordinator>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(sp => new TrainingDataInitializer(
            sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<IndexedDbStorageService>()));
        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<IWorkoutPlanService, WorkoutPlanService>();
        services.AddScoped<IWorkoutSessionService, WorkoutSessionService>();
        services.AddSingleton<INutritionCalculator, NutritionCalculator>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IFoodService, FoodService>();
        services.AddScoped<INutritionService, NutritionService>();
        // Keyed DI supplies a separately named browser HttpClient for each provider.
        services.AddKeyedScoped<HttpClient>(OpenFoodFactsProvider.ClientName, (_, _) => new HttpClient
        {
            BaseAddress = new Uri("https://world.openfoodfacts.org/"), Timeout = TimeSpan.FromSeconds(10)
        });
        services.AddKeyedScoped<HttpClient>(UsdaProvider.ClientName, (_, _) => new HttpClient
        {
            BaseAddress = new Uri("https://api.nal.usda.gov/fdc/v1/"), Timeout = TimeSpan.FromSeconds(10)
        });
        services.AddScoped<SearchRequestPolicy>();
        services.AddScoped<SearchCacheService>();
        services.AddScoped<IBrowserConnectivity, BrowserConnectivity>();
        services.AddScoped<OpenFoodFactsProvider>(sp => new(sp.GetRequiredKeyedService<HttpClient>(OpenFoodFactsProvider.ClientName), sp.GetRequiredService<SearchRequestPolicy>(), options.Native));
        services.AddScoped<UsdaProvider>(sp => new(sp.GetRequiredKeyedService<HttpClient>(UsdaProvider.ClientName), sp.GetRequiredService<IProfileService>()));
        services.AddScoped<FoodSearchService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IWaterService, WaterService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<IRecipeService, RecipeService>();
        services.AddScoped<IPlanImportService, PlanImportService>();
        services.AddScoped<INutritionImportService, NutritionImportService>();
        return services;
    }
}
