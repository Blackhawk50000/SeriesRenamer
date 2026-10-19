using System.ComponentModel;
using System.Runtime.CompilerServices;
using SeriesRenamer.Services;

namespace SeriesRenamer.Models;

public enum FileState
{
    Pending,
    Ready,
    Unchanged,
    Renamed,
    Error,
}

/// <summary>Eine Datei in der Liste: links der Originalname, rechts der neue Name.</summary>
public sealed class MediaFile : INotifyPropertyChanged
{
    private string _fullPath;
    private TvShow? _show;
    private string _newName = "";
    private string _status = "";
    private FileState _state;

    public MediaFile(string fullPath)
    {
        _fullPath = fullPath;
        Parsed = FileNameParser.Parse(fullPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ParsedName Parsed { get; }

    public string FullPath
    {
        get => _fullPath;
        set
        {
            _fullPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OriginalName));
        }
    }

    public string OriginalName => Path.GetFileName(FullPath);
    public string Folder => Path.GetDirectoryName(FullPath)!;
    public string Extension => Path.GetExtension(FullPath);

    public TvShow? Show
    {
        get => _show;
        set
        {
            _show = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetectionInfo));
        }
    }

    /// <summary>Serie wurde vom Benutzer festgelegt und wird beim automatischen Abgleich nicht überschrieben.</summary>
    public bool ShowAssignedManually { get; set; }

    public IReadOnlyList<string> EpisodeTitles { get; set; } = [];

    /// <summary>Fehler beim TMDb-Abgleich oder beim Umbenennen; wird beim nächsten Abgleich zurückgesetzt.</summary>
    public string? LookupError { get; set; }

    public bool JustRenamed { get; set; }

    public string DetectionInfo
    {
        get
        {
            if (Parsed.Season is null)
                return "Keine Staffel/Episode erkannt";

            var episode = Parsed.EpisodeEnd is null
                ? $"S{Parsed.Season:00}E{Parsed.Episode:00}"
                : $"S{Parsed.Season:00}E{Parsed.Episode:00}-E{Parsed.EpisodeEnd:00}";
            var series = Show is not null
                ? $"TMDb: {Show}"
                : $"Erkannt: {Parsed.SeriesGuess ?? "?"}";
            return $"{episode}  ·  {series}";
        }
    }

    public string NewName
    {
        get => _newName;
        private set { _newName = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public FileState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); }
    }

    public void SetResult(string newName, FileState state, string status)
    {
        NewName = newName;
        State = state;
        Status = status;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
