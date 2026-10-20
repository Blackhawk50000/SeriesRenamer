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
/// Alternative Ausstrahlungsreihenfolge einer Serie (bei TMDb "Episode Group"), z. B. DVD- oder
/// Produktionsreihenfolge. Id <c>null</c> steht für die normale TMDb-Staffelzählung.
/// </summary>
public sealed record EpisodeOrder(string? Id, string Name, int Type, int EpisodeCount, int GroupCount)
{
    public static readonly EpisodeOrder Default = new(null, "Standard (TMDb-Staffeln)", 0, 0, 0);

    public string TypeName => Type switch
    {
        1 => "Ausstrahlung",
        2 => "Absolut",
        3 => "DVD",
        4 => "Digital",
        5 => "Handlungsbogen",
        6 => "Produktion",
        7 => "TV",
        _ => "",
    };

    public override string ToString() => Id is null
        ? Name
        : $"{Name}  ({TypeName}, {GroupCount} Staffeln, {EpisodeCount} Episoden)";
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
    private readonly Dictionary<int, IReadOnlyList<EpisodeOrder>> _ordersCache = new();
    private readonly Dictionary<string, IReadOnlyDictionary<(int Season, int Episode), string>> _orderEpisodesCache = new();

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

    /// <summary>Liefert die verfügbaren Reihenfolgen einer Serie; der erste Eintrag ist immer <see cref="EpisodeOrder.Default"/>.</summary>
    public async Task<IReadOnlyList<EpisodeOrder>> GetEpisodeOrdersAsync(int showId)
    {
        if (_ordersCache.TryGetValue(showId, out var cached))
            return cached;

        using var doc = await GetJsonAsync($"tv/{showId}/episode_groups?language={Language}");
        var orders = new List<EpisodeOrder> { EpisodeOrder.Default };
        orders.AddRange(doc.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => new EpisodeOrder(
                GetString(r, "id"),
                GetString(r, "name"),
                GetInt(r, "type"),
                GetInt(r, "episode_count"),
                GetInt(r, "group_count")))
            .OrderBy(o => o.Type)
            .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase));

        _ordersCache[showId] = orders;
        return orders;
    }

    /// <summary>
    /// Liefert (Staffel, Episode) -> Episodentitel in einer alternativen Reihenfolge.
    /// Wie bei Kodi/Jellyfin entspricht die Staffel dem "order" der Gruppe, die Episode dem
    /// (0-basierten) "order" innerhalb der Gruppe plus 1.
    /// </summary>
    public async Task<IReadOnlyDictionary<(int Season, int Episode), string>> GetEpisodeOrderAsync(string orderId)
    {
        if (_orderEpisodesCache.TryGetValue(orderId, out var cached))
            return cached;

        using var doc = await GetJsonAsync($"tv/episode_group/{Uri.EscapeDataString(orderId)}?language={Language}");
        var episodes = new Dictionary<(int, int), string>();
        foreach (var group in doc.RootElement.GetProperty("groups").EnumerateArray())
        {
            var season = GetInt(group, "order");
            foreach (var ep in group.GetProperty("episodes").EnumerateArray())
                episodes[(season, GetInt(ep, "order") + 1)] = GetString(ep, "name");
        }

        _orderEpisodesCache[orderId] = episodes;
        return episodes;
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

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    public void Dispose() => _http.Dispose();
}
