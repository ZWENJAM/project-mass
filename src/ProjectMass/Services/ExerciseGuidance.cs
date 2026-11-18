using ProjectMass.Models;

namespace ProjectMass.Services;

public static class ExerciseGuidance
{
    public static string? HttpsVideoUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0
            ? uri.AbsoluteUri : null;

    public static string VideoLink(Exercise exercise) =>
        HttpsVideoUrl(exercise.VideoUrl) ??
        "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(exercise.Name + " richtige Ausführung");

    public static bool FillMissing(Exercise target, Exercise source)
    {
        var changed = false;
        if (!HasText(target.Steps) && HasText(source.Steps)) { target.Steps = source.Steps; changed = true; }
        if (!HasText(target.Tips) && HasText(source.Tips)) { target.Tips = source.Tips; changed = true; }
        if (!HasText(target.Mistakes) && HasText(source.Mistakes)) { target.Mistakes = source.Mistakes; changed = true; }
        if (string.IsNullOrWhiteSpace(target.ImagePath) && !string.IsNullOrWhiteSpace(source.ImagePath)) { target.ImagePath = source.ImagePath; changed = true; }
        if (string.IsNullOrWhiteSpace(target.ImagePath2) && !string.IsNullOrWhiteSpace(source.ImagePath2)) { target.ImagePath2 = source.ImagePath2; changed = true; }
        if (string.IsNullOrWhiteSpace(target.VideoUrl) && HttpsVideoUrl(source.VideoUrl) is { } url) { target.VideoUrl = url; changed = true; }
        return changed;
    }

    private static bool HasText(List<string>? lines) => lines?.Any(line => !string.IsNullOrWhiteSpace(line)) == true;
}
