using ProjectMass.Models;

namespace ProjectMass.Components;

public static class TrainingLabels
{
    public static string Day(DayOfWeek? day) => day switch
    {
        DayOfWeek.Monday => "Montag", DayOfWeek.Tuesday => "Dienstag", DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag", DayOfWeek.Friday => "Freitag", DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday => "Sonntag", _ => "Flexibel"
    };
    public static string Set(SetType type) => type switch { SetType.WarmUp => "Aufwärmen", SetType.Drop => "Drop", _ => "Arbeit" };
}
