using ProjectMass.Models;

namespace ProjectMass.Components;

public static class NutritionLabels
{
    public static string Meal(MealType meal) => meal switch { MealType.Breakfast => "Frühstück", MealType.Lunch => "Mittag", MealType.Dinner => "Abend", _ => "Snack" };
    public static string Gender(Gender gender) => gender switch { Models.Gender.Male => "Männlich", Models.Gender.Female => "Weiblich", Models.Gender.Diverse => "Divers", _ => "Keine Angabe" };
    public static string Activity(ActivityLevel level) => level switch { ActivityLevel.Sedentary => "Sitzend / 1,2", ActivityLevel.Light => "Leicht aktiv / 1,375", ActivityLevel.Moderate => "Moderat aktiv / 1,55", ActivityLevel.Active => "Aktiv / 1,725", _ => "Sehr aktiv / 1,9" };
    public static string Goal(FitnessGoal goal) => goal switch { FitnessGoal.Build => "Aufbau", FitnessGoal.Lose => "Diät", _ => "Halten" };
}
