using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SeriesRenamer.Services;

public sealed record TvShow(int Id, string Name, string OriginalName, string FirstAirDate)
{
    public string Year => FirstAirDate.Length >= 4 ? FirstAirDate[..4] : "";

    public override string ToString() => Year.Length > 0 ? $"{Name} ({Year})" : Name;
}

/// <summary>
/// Minimaler Client für die TMDb-API v3. Akzeptiert sowohl den v3-API-Key (32 Zeichen)
/// als auch den v4 "API Read Access Token" (wird als Bearer-Token gesendet).
/// Ergebnisse werden pro Instanz zwischengespeichert; nur vom UI-Thread verwenden.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _useBearer;
    private readonly Dictionary<string, IReadOnlyList<TvShow>> _searchCache = new();
    private readonly Dictionary<(int ShowId, int Season), IReadOnlyDictionary<int, string>> _seasonCache = new();

    public string ApiKey { get; }
    public string Language { get; }

    public TmdbClient(string apiKey, string language)
    {
        ApiKey = apiKey;
        Language = language;
        _useBearer = apiKey.Length > 40;

        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_useBearer)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<IReadOnlyList<TvShow>> SearchTvAsync(string query, int? year)
    {
        var cacheKey = $"{query.ToLowerInvariant()}|{year}";
        if (_searchCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url = $"search/tv?query={Uri.EscapeDataString(query)}&language={Language}";
        if (year is not null)
            url += $"&first_air_date_year={year}";

        using var doc = await GetJsonAsync(url);
        var results = doc.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => new TvShow(
                r.GetProperty("id").GetInt32(),
                GetString(r, "name"),
                GetString(r, "original_name"),
                GetString(r, "first_air_date")))
            .ToList();

        // Jahr aus dem Dateinamen war evtl. falsch -> ohne Jahr nochmal versuchen
        if (results.Count == 0 && year is not null)
            return await SearchTvAsync(query, null);

        _searchCache[cacheKey] = results;
        return results;
    }

    /// <summary>Liefert Episodennummer -> Episodentitel einer Staffel.</summary>
    public async Task<IReadOnlyDictionary<int, string>> GetSeasonAsync(int showId, int season)
    {
        if (_seasonCache.TryGetValue((showId, season), out var cached))
            return cached;

        JsonDocument doc;
        try
        {
            doc = await GetJsonAsync($"tv/{showId}/season/{season}?language={Language}");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Staffel {season} existiert bei TMDb nicht.");
        }

        using (doc)
        {
            var episodes = new Dictionary<int, string>();
            foreach (var ep in doc.RootElement.GetProperty("episodes").EnumerateArray())
                episodes[ep.GetProperty("episode_number").GetInt32()] = GetString(ep, "name");

            _seasonCache[(showId, season)] = episodes;
            return episodes;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl)
    {
        if (!_useBearer)
            relativeUrl += (relativeUrl.Contains('?') ? "&" : "?") + "api_key=" + Uri.EscapeDataString(ApiKey);

        using var response = await _http.GetAsync(relativeUrl);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("TMDb hat den API-Key abgelehnt (401). Bitte Key prüfen.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    public void Dispose() => _http.Dispose();
}
