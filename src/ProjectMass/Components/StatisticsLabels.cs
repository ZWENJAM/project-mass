using ProjectMass.Models;

namespace ProjectMass.Components;

public static class StatisticsLabels
{
    public static string Record(RecordKind kind) => kind switch { RecordKind.EstimatedOneRepMax => "e1RM", RecordKind.Weight => "Höchstes Gewicht", _ => "Meiste Wiederholungen" };
    public static string Unit(RecordKind kind) => kind == RecordKind.Reps ? "Wdh." : "kg";
    public static string Value(decimal? value, string unit = "") => value.HasValue ? $"{value:0.#} {unit}".TrimEnd() : "—";
}
