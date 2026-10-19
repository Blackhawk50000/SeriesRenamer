using System.Text.RegularExpressions;
using SeriesRenamer.Models;

namespace SeriesRenamer.Services;

/// <summary>
/// Setzt das Benennungsmuster um. Platzhalter (Groß-/Kleinschreibung egal):
///   {Serienname} {Staffel} {Episode} {SxxExx} {EpisodenTitel} {Jahr} {Original}
/// Zahlen lassen sich formatieren, z. B. {Staffel:00} -> "01", {Episode:0} -> "5".
/// </summary>
public static partial class NamePattern
{
    public const string Default = "{Serienname} - S{Staffel:00}E{Episode:00} - {EpisodenTitel}";

    public const string Help =
        "Platzhalter: {Serienname}  {Staffel}  {Episode}  {SxxExx}  {EpisodenTitel}  {Jahr}  {Original}" +
        "   –   Zahlenformat z. B. {Staffel:00} → 01, {Episode:0} → 5";

    [GeneratedRegex(@"\{(?<name>\w+)(?::(?<fmt>[^}]+))?\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    /// <summary>Erzeugt den neuen Dateinamen inkl. Original-Endung.</summary>
    public static string Apply(string pattern, MediaFile file)
    {
        var parsed = file.Parsed;
        var name = TokenRegex().Replace(pattern, m =>
        {
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            return m.Groups["name"].Value.ToLowerInvariant() switch
            {
                "serienname" or "serie" => file.Show?.Name ?? parsed.SeriesGuess ?? "",
                "staffel" or "season" => Number(parsed.Season, fmt ?? "00"),
                "episode" => EpisodeRange(parsed, fmt ?? "00"),
                "sxxexx" => $"S{Number(parsed.Season, "00")}E{EpisodeRange(parsed, "00")}",
                "episodentitel" or "titel" or "title" => string.Join(" & ", file.EpisodeTitles),
                "jahr" or "year" => file.Show?.Year ?? parsed.Year?.ToString() ?? "",
                "original" => Path.GetFileNameWithoutExtension(file.OriginalName),
                _ => m.Value,
            };
        });

        return Sanitize(name) + file.Extension;
    }

    private static string Number(int? value, string format) =>
        value is null ? "" : value.Value.ToString(format);

    // Doppelfolgen: "05-E06"
    private static string EpisodeRange(ParsedName parsed, string format) =>
        parsed.EpisodeEnd is null
            ? Number(parsed.Episode, format)
            : $"{Number(parsed.Episode, format)}-E{Number(parsed.EpisodeEnd, format)}";

    /// <summary>Entfernt bzw. ersetzt Zeichen, die in Windows-Dateinamen nicht erlaubt sind.</summary>
    private static string Sanitize(string name)
    {
        name = name
            .Replace(": ", " - ")
            .Replace(':', '-')
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace('"', '\'')
            .Replace('|', '-');

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "");

        return MultiSpaceRegex().Replace(name, " ").Trim().TrimEnd('.', ' ');
    }
}
