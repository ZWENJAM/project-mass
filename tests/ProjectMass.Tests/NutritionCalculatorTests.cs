using ProjectMass.Models;
using ProjectMass.Services;

namespace ProjectMass.Tests;

public class NutritionCalculatorTests
{
    private readonly NutritionCalculator calculator = new();

    // 80 kg, 180 cm, 30 Jahre: 10*80 + 6.25*180 - 5*30 = 1775.
    // Männlich: 1775+5=1780; weiblich: 1775-161=1614; Mittelwert: 1775-78=1697.
    [Theory]
    [InlineData(Gender.Male, 1780)]
    [InlineData(Gender.Female, 1614)]
    [InlineData(Gender.Diverse, 1697)]
    [InlineData(Gender.Unspecified, 1697)]
    public void MifflinStJeorHasSpecifiedGenderOffsets(Gender gender, int expected) =>
        Assert.Equal(expected, calculator.CalculateBmr(gender, 80, 180, 30));

    [Theory]
    [InlineData(ActivityLevel.Sedentary, "1.2")]
    [InlineData(ActivityLevel.Light, "1.375")]
    [InlineData(ActivityLevel.Moderate, "1.55")]
    [InlineData(ActivityLevel.Active, "1.725")]
    [InlineData(ActivityLevel.VeryActive, "1.9")]
    public void ActivityAndAllGoalAdjustmentsAreExact(ActivityLevel activity, string factorText)
    {
        var factor = decimal.Parse(factorText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(factor, calculator.ActivityFactor(activity));
        foreach (var (goal, adjustment) in new[] { (FitnessGoal.Build, 300), (FitnessGoal.Maintain, 0), (FitnessGoal.Lose, -500) })
        {
            var result = calculator.CalculateTargets(Gender.Male, 80, 180, 30, activity, goal);
            Assert.Equal(1780m * factor, result.MaintenanceKcal);
            Assert.Equal(1780m * factor + adjustment, result.Kcal);
        }
    }

    [Fact]
    public void MacrosUseWeightAndRemainingCaloriesWithoutRounding()
    {
        var result = calculator.CalculateTargets(Gender.Male, 80, 180, 30, ActivityLevel.Moderate, FitnessGoal.Build);
        // 1780*1.55+300=3059; Protein=160g, Fett=64g; KH=(3059-640-576)/4=460.75g.
        Assert.Equal(3059m, result.Kcal);
        Assert.Equal(160m, result.ProteinGrams);
        Assert.Equal(64m, result.FatGrams);
        Assert.Equal(460.75m, result.CarbohydrateGrams);
        Assert.False(result.HasInsufficientCalories);
    }

    [Fact]
    public void NegativeRemainingCaloriesSetCarbsToZeroAndFlagWarning()
    {
        var result = calculator.CalculateTargets(Gender.Female, 200, 150, 80, ActivityLevel.Sedentary, FitnessGoal.Lose);
        // BMR=2000+937.5-400-161=2376.5; Ziel=2351.8; Protein/Fett benötigen 3040 kcal.
        Assert.Equal(2351.8m, result.Kcal);
        Assert.Equal(400m, result.ProteinGrams);
        Assert.Equal(160m, result.FatGrams);
        Assert.Equal(0m, result.CarbohydrateGrams);
        Assert.True(result.HasInsufficientCalories);
    }

    [Fact]
    public void AmountCalculationIncludesEveryNutrientAndDoesNotRound()
    {
        var original = new Nutrients { Kcal = 333.33m, ProteinGrams = 13.37m, CarbohydrateGrams = 55.55m, FatGrams = 4.44m, FiberGrams = 9.99m, SugarGrams = 1.23m, SaltGrams = .017m };
        var result = calculator.ForAmount(original, 37.5m);
        Assert.Equal(124.99875m, result.Kcal);
        Assert.Equal(5.01375m, result.ProteinGrams);
        Assert.Equal(20.83125m, result.CarbohydrateGrams);
        Assert.Equal(1.665m, result.FatGrams);
        Assert.Equal(3.74625m, result.FiberGrams);
        Assert.Equal(.46125m, result.SugarGrams);
        Assert.Equal(.006375m, result.SaltGrams);
        Assert.Equal(333.33m, original.Kcal);
        Assert.Equal(new Nutrients(), calculator.ForAmount(original, 0));
        Assert.Throws<ArgumentException>(() => calculator.ForAmount(original, -1));
    }
}
