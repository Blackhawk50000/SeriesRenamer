using System.Text.RegularExpressions;

namespace SeriesRenamer.Services;

/// <summary>Was aus einem Dateinamen herausgelesen werden konnte.</summary>
public sealed record ParsedName(string? SeriesGuess, int? Year, int? Season, int? Episode, int? EpisodeEnd);

public static partial class FileNameParser
{
    // S01E05, s1e5, S01.E05, S01E05E06, S01E05-E06
    [GeneratedRegex(@"[Ss](?<s>\d{1,2})[\s._-]*[Ee](?<e>\d{1,3})(?:[\s._-]*-?[Ee](?<e2>\d{1,3}))?")]
    private static partial Regex SeasonEpisodeRegex();

    // 1x05, 01x05
    [GeneratedRegex(@"(?<!\d)(?<s>\d{1,2})x(?<e>\d{2,3})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex CrossFormatRegex();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"[\s(]*\b(?<y>(?:19|20)\d{2})\)?\s*$")]
    private static partial Regex TrailingYearRegex();

    [GeneratedRegex(@"^(season|staffel|s)\s*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static ParsedName Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        var match = SeasonEpisodeRegex().Match(name);
        if (!match.Success)
            match = CrossFormatRegex().Match(name);
        if (!match.Success)
            return new ParsedName(GuessFromFolder(path, out var y), y, null, null, null);

        int season = int.Parse(match.Groups["s"].Value);
        int episode = int.Parse(match.Groups["e"].Value);
        int? episodeEnd = match.Groups["e2"].Success ? int.Parse(match.Groups["e2"].Value) : null;
        if (episodeEnd <= episode)
            episodeEnd = null;

        var guess = Clean(name[..match.Index], out var year);
        if (string.IsNullOrWhiteSpace(guess))
            guess = GuessFromFolder(path, out year);

        return new ParsedName(guess, year, season, episode, episodeEnd);
    }

    /// <summary>Nutzt den Ordnernamen (z. B. "Breaking Bad\Season 1\05.mkv") als Serienhinweis.</summary>
    private static string? GuessFromFolder(string path, out int? year)
    {
        year = null;
        var dir = Path.GetDirectoryName(path);
        if (dir is null)
            return null;

        var folder = Path.GetFileName(dir);
        if (SeasonFolderRegex().IsMatch(folder))
            folder = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");

        var guess = Clean(folder, out year);
        return string.IsNullOrWhiteSpace(guess) ? null : guess;
    }

    private static string Clean(string raw, out int? year)
    {
        year = null;
        var s = BracketRegex().Replace(raw, " ").Replace('.', ' ').Replace('_', ' ');
        s = WhitespaceRegex().Replace(s, " ").Trim(' ', '-');

        var yearMatch = TrailingYearRegex().Match(s);
        if (yearMatch.Success)
        {
            var withoutYear = s[..yearMatch.Index].Trim(' ', '-');
            // Serien wie "1923" nicht komplett wegschneiden
            if (withoutYear.Length > 0)
            {
                year = int.Parse(yearMatch.Groups["y"].Value);
                s = withoutYear;
            }
        }

        return s;
    }
}
