using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ProjectMass;
using ProjectMass.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IndexedDbStorageService>();
builder.Services.AddScoped(sp => new ChangeTrackingStorageService(
    sp.GetRequiredService<IndexedDbStorageService>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<IStorageService>(sp => sp.GetRequiredService<ChangeTrackingStorageService>());
builder.Services.AddScoped<NasBrowser>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<NasSyncCoordinator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped(sp => new TrainingDataInitializer(
    sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<IndexedDbStorageService>()));
builder.Services.AddScoped<IExerciseService, ExerciseService>();
builder.Services.AddScoped<IWorkoutPlanService, WorkoutPlanService>();
builder.Services.AddScoped<IWorkoutSessionService, WorkoutSessionService>();
builder.Services.AddSingleton<INutritionCalculator, NutritionCalculator>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFoodService, FoodService>();
builder.Services.AddScoped<INutritionService, NutritionService>();
builder.Services.AddScoped(sp => new NutritionDataInitializer(
    sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<IndexedDbStorageService>()));
// Keyed DI supplies a separately named browser HttpClient for each provider.
builder.Services.AddKeyedScoped<HttpClient>(OpenFoodFactsProvider.ClientName, (_, _) => new HttpClient
{
    BaseAddress = new Uri("https://world.openfoodfacts.org/"), Timeout = TimeSpan.FromSeconds(10)
});
builder.Services.AddKeyedScoped<HttpClient>(UsdaProvider.ClientName, (_, _) => new HttpClient
{
    BaseAddress = new Uri("https://api.nal.usda.gov/fdc/v1/"), Timeout = TimeSpan.FromSeconds(10)
});
builder.Services.AddScoped<SearchRequestPolicy>();
builder.Services.AddScoped<SearchCacheService>();
builder.Services.AddScoped<IBrowserConnectivity, BrowserConnectivity>();
builder.Services.AddScoped<OpenFoodFactsProvider>(sp => new(sp.GetRequiredKeyedService<HttpClient>(OpenFoodFactsProvider.ClientName), sp.GetRequiredService<SearchRequestPolicy>()));
builder.Services.AddScoped<UsdaProvider>(sp => new(sp.GetRequiredKeyedService<HttpClient>(UsdaProvider.ClientName), sp.GetRequiredService<IProfileService>()));
builder.Services.AddScoped<FoodSearchService>();
builder.Services.AddSingleton<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IMeasurementService, MeasurementService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IWaterService, WaterService>();
builder.Services.AddScoped<INoteService, NoteService>();
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IPlanImportService, PlanImportService>();
builder.Services.AddScoped<INutritionImportService, NutritionImportService>();

await builder.Build().RunAsync();
