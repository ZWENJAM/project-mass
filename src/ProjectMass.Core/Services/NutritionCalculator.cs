using ProjectMass.Models;

namespace ProjectMass.Services;

public sealed class NutritionCalculator : INutritionCalculator
{
    public decimal CalculateBmr(Gender gender, decimal weightKg, decimal heightCm, int age)
    {
        if (weightKg <= 0 || heightCm <= 0 || age is < 1 or > 120)
            throw new ArgumentException("Für die Berechnung werden ein gültiges Gewicht, eine Größe und ein Alter von 1–120 Jahren benötigt.");
        var offset = gender switch { Gender.Male => 5m, Gender.Female => -161m, _ => -78m };
        return 10m * weightKg + 6.25m * heightCm - 5m * age + offset;
    }

    public decimal ActivityFactor(ActivityLevel activity) => activity switch
    {
        ActivityLevel.Sedentary => 1.2m, ActivityLevel.Light => 1.375m,
        ActivityLevel.Moderate => 1.55m, ActivityLevel.Active => 1.725m,
        ActivityLevel.VeryActive => 1.9m, _ => throw new ArgumentException("Ungültiges Aktivitätslevel.")
    };

    public NutritionTargets CalculateTargets(Gender gender, decimal weightKg, decimal heightCm, int age, ActivityLevel activity, FitnessGoal goal)
    {
        var bmr = CalculateBmr(gender, weightKg, heightCm, age);
        var maintenance = bmr * ActivityFactor(activity);
        var adjustment = goal switch { FitnessGoal.Build => 300m, FitnessGoal.Maintain => 0m, FitnessGoal.Lose => -500m, _ => throw new ArgumentException("Ungültiges Ziel.") };
        var kcal = maintenance + adjustment;
        var protein = 2m * weightKg;
        var fat = .8m * weightKg;
        var remaining = kcal - protein * 4m - fat * 9m;
        return new(bmr, maintenance, kcal, protein, Math.Max(0m, remaining / 4m), fat, remaining < 0m);
    }

    public Nutrients ForAmount(Nutrients per100G, decimal grams)
    {
        if (grams < 0) throw new ArgumentException("Die Menge darf nicht negativ sein.");
        var factor = grams / 100m;
        return new Nutrients
        {
            Kcal = per100G.Kcal * factor, ProteinGrams = per100G.ProteinGrams * factor,
            CarbohydrateGrams = per100G.CarbohydrateGrams * factor, FatGrams = per100G.FatGrams * factor,
            FiberGrams = per100G.FiberGrams * factor, SugarGrams = per100G.SugarGrams * factor, SaltGrams = per100G.SaltGrams * factor
        };
    }

    public Nutrients Sum(IEnumerable<Nutrients> values)
    {
        var sum = new Nutrients();
        foreach (var n in values)
        {
            sum.Kcal += n.Kcal; sum.ProteinGrams += n.ProteinGrams;
            sum.CarbohydrateGrams += n.CarbohydrateGrams; sum.FatGrams += n.FatGrams;
            sum.FiberGrams += n.FiberGrams; sum.SugarGrams += n.SugarGrams; sum.SaltGrams += n.SaltGrams;
        }
        return sum;
    }
}
