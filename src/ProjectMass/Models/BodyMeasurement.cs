namespace ProjectMass.Models;

public sealed class BodyMeasurement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? WeightKg { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? UpperArmCm { get; set; }
    public decimal? BodyFatPercent { get; set; }
}
