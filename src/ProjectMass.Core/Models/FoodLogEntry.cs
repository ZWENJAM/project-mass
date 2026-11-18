namespace ProjectMass.Models;

public enum MealType { Breakfast, Lunch, Dinner, Snack }

public sealed class FoodLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public MealType Meal { get; set; }
    // Owned snapshot; later edits to the food catalog must not change the log.
    public FoodItem FoodSnapshot { get; set; } = new();
    public decimal AmountGrams { get; set; }
    public bool IsQuickEntry { get; set; }
    public DateTimeOffset LoggedAt { get; set; } = DateTimeOffset.UtcNow;
}
