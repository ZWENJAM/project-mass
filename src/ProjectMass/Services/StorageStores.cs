namespace ProjectMass.Services;

// Children are persisted only as part of their aggregate.
// Keep these names in sync with wwwroot/js/db.js.
public static class StorageStores
{
    public const string SearchCache = "searchCache";
    public const string UserProfiles = "userProfiles";
    public const string Exercises = "exercises";
    public const string WorkoutPlans = "workoutPlans";
    public const string WorkoutSessions = "workoutSessions";
    public const string FoodItems = "foodItems";
    public const string FoodLogEntries = "foodLogEntries";
    public const string Recipes = "recipes";
    public const string AppMetadata = "appMetadata";
    public const string BodyMeasurements = "bodyMeasurements";
    public const string WaterLogs = "waterLogs";
    public const string Notes = "notes";
    public const string SyncMetadata = "syncMetadata";

    public static bool TracksChanges(string store) => store is
        UserProfiles or Exercises or WorkoutPlans or WorkoutSessions or FoodItems or FoodLogEntries
        or Recipes or BodyMeasurements or WaterLogs or Notes;
}
