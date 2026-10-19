using System.Text.Json;

namespace SeriesRenamer.Services;

/// <summary>Einstellungen unter %AppData%\SeriesRenamer\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeriesRenamer", "settings.json");

    public string ApiKey { get; set; } = "";
    public string Language { get; set; } = "de-DE";
    public string Pattern { get; set; } = NamePattern.Default;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Defekte Datei -> mit Standardwerten weitermachen
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
