using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SeriesRenamer.Models;
using SeriesRenamer.Services;

namespace SeriesRenamer;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".flv",
        ".srt", ".ass", ".ssa", ".sub", ".idx",
    };

    private readonly ObservableCollection<MediaFile> _files = [];
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly List<(MediaFile File, string From, string To)> _lastRenames = [];
    private TmdbClient? _client;
    private ScrollViewer? _leftScroll, _rightScroll;
    private bool _syncingSelection, _syncingScroll;

    public MainWindow()
    {
        InitializeComponent();

        LeftList.ItemsSource = _files;
        RightList.ItemsSource = _files;
        _files.CollectionChanged += (_, _) => DropHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        PatternHelp.Text = NamePattern.Help;
        ApiKeyBox.Password = _settings.ApiKey;
        LanguageBox.Text = _settings.Language;
        PatternBox.Text = _settings.Pattern;

        Loaded += (_, _) => HookScrollSync();
        UpdateStatusText();
    }

    // ---------- Drag & Drop ----------

    private void LeftList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void LeftList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        var known = new HashSet<string>(_files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        var added = new List<MediaFile>();

        foreach (var path in ExpandPaths(paths).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!known.Add(path))
                continue;
            var file = new MediaFile(path);
            _files.Add(file);
            added.Add(file);
        }

        RefreshNames();
        if (added.Count > 0 && ApiKeyBox.Password.Trim().Length > 0)
            await MatchAsync(added);
    }

    /// <summary>Einzelne Dateien werden immer übernommen, Ordner rekursiv nach Video-/Untertiteldateien durchsucht.</summary>
    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return path;
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    if (MediaExtensions.Contains(Path.GetExtension(file)))
                        yield return file;
            }
        }
    }

    // ---------- TMDb ----------

    private TmdbClient? GetClient()
    {
        var key = ApiKeyBox.Password.Trim();
        var language = LanguageBox.Text.Trim();
        if (language.Length == 0)
            language = "de-DE";

        if (key.Length == 0)
        {
            MessageBox.Show(this,
                "Bitte zuerst einen TMDb API-Key eintragen.\n\nKostenlos erhältlich unter themoviedb.org → Einstellungen → API.",
                "API-Key fehlt", MessageBoxButton.OK, MessageBoxImage.Information);
            ApiKeyBox.Focus();
            return null;
        }

        if (_client is null || _client.ApiKey != key || _client.Language != language)
        {
            _client?.Dispose();
            _client = new TmdbClient(key, language);
        }
        return _client;
    }

    private async void Match_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedFiles();
        await MatchAsync(targets.Count > 0 ? targets : _files.ToList());
    }

    /// <summary>Sucht für automatisch erkannte Serien den TMDb-Eintrag und lädt dann die Episodentitel.</summary>
    private async Task MatchAsync(IReadOnlyList<MediaFile> targets)
    {
        var client = GetClient();
        if (client is null)
            return;

        SetBusy(true, "Gleiche mit TMDb ab …");
        try
        {
            var groups = targets
                .Where(f => !f.ShowAssignedManually && f.Parsed.Season is not null)
                .GroupBy(f => (f.Parsed.SeriesGuess ?? "").ToLowerInvariant());

            foreach (var group in groups)
            {
                var first = group.First();
                if (string.IsNullOrWhiteSpace(first.Parsed.SeriesGuess))
                {
                    foreach (var f in group)
                    {
                        f.Show = null;
                        f.LookupError = "Serienname nicht erkennbar – bitte oben manuell festlegen";
                    }
                    continue;
                }

                try
                {
                    var results = await client.SearchTvAsync(first.Parsed.SeriesGuess, first.Parsed.Year);
                    var show = results.FirstOrDefault();
                    foreach (var f in group)
                    {
                        f.Show = show;
                        f.LookupError = show is null ? $"Keine Serie „{first.Parsed.SeriesGuess}“ bei TMDb gefunden" : null;
                    }
                }
                catch (Exception ex)
                {
                    foreach (var f in group)
                        f.LookupError = $"TMDb-Fehler: {ex.Message}";
                }
            }

            await LoadEpisodeTitlesAsync(client, targets);
        }
        finally
        {
            SetBusy(false);
            RefreshNames();
        }
    }

    private static async Task LoadEpisodeTitlesAsync(TmdbClient client, IEnumerable<MediaFile> targets)
    {
        foreach (var file in targets)
        {
            var parsed = file.Parsed;
            if (file.Show is null || parsed.Season is null || parsed.Episode is null)
                continue;

            try
            {
                var episodes = await client.GetSeasonAsync(file.Show.Id, parsed.Season.Value);
                var numbers = Enumerable.Range(parsed.Episode.Value, (parsed.EpisodeEnd ?? parsed.Episode.Value) - parsed.Episode.Value + 1);
                var titles = new List<string>();
                string? error = null;

                foreach (var n in numbers)
                {
                    if (episodes.TryGetValue(n, out var title))
                        titles.Add(title);
                    else
                        error = $"Episode {n} in Staffel {parsed.Season} bei TMDb nicht gefunden";
                }

                file.EpisodeTitles = titles;
                file.LookupError = error;
            }
            catch (Exception ex)
            {
                file.EpisodeTitles = [];
                file.LookupError = $"TMDb-Fehler: {ex.Message}";
            }
        }
    }

    // ---------- Serie manuell festlegen ----------

    private void SeriesQueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SeriesSearch_Click(sender, e);
    }

    private async void SeriesSearch_Click(object sender, RoutedEventArgs e)
    {
        var query = SeriesQueryBox.Text.Trim();
        if (query.Length == 0)
        {
            // Vorschlag aus der markierten Datei übernehmen
            query = SelectedFiles().FirstOrDefault()?.Parsed.SeriesGuess ?? "";
            SeriesQueryBox.Text = query;
            if (query.Length == 0)
                return;
        }

        var client = GetClient();
        if (client is null)
            return;

        SetBusy(true, $"Suche „{query}“ …");
        try
        {
            var results = await client.SearchTvAsync(query, null);
            SeriesResults.ItemsSource = results;
            SeriesResults.SelectedIndex = results.Count > 0 ? 0 : -1;
            SeriesResults.IsDropDownOpen = results.Count > 1;
            if (results.Count == 0)
                MessageBox.Show(this, $"Keine Serie zu „{query}“ gefunden.", "Suche", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TMDb-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AssignSeries_Click(object sender, RoutedEventArgs e)
    {
        if (SeriesResults.SelectedItem is not TvShow show)
        {
            MessageBox.Show(this, "Bitte zuerst eine Serie suchen und auswählen.", "Serie festlegen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var client = GetClient();
        if (client is null)
            return;

        var targets = SelectedFiles();
        if (targets.Count == 0)
            targets = _files.ToList();

        foreach (var f in targets)
        {
            f.Show = show;
            f.ShowAssignedManually = true;
            f.LookupError = null;
        }

        SetBusy(true, $"Lade Episoden von {show.Name} …");
        try
        {
            await LoadEpisodeTitlesAsync(client, targets);
        }
        finally
        {
            SetBusy(false);
            RefreshNames();
        }
    }

    // ---------- Namen berechnen ----------

    private void PatternBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshNames();

    private void RefreshNames()
    {
        var pattern = PatternBox.Text;

        foreach (var file in _files)
        {
            if (file.Parsed.Season is null)
                file.SetResult("—", FileState.Error, "Keine Staffel/Episode im Dateinamen gefunden");
            else if (file.LookupError is not null)
                file.SetResult("—", FileState.Error, file.LookupError);
            else if (file.Show is null || file.EpisodeTitles.Count == 0)
                file.SetResult("—", FileState.Pending, "Noch nicht mit TMDb abgeglichen");
            else
            {
                var newName = NamePattern.Apply(pattern, file);
                if (Path.GetFileNameWithoutExtension(newName).Length == 0)
                    file.SetResult("—", FileState.Error, "Muster ergibt einen leeren Namen");
                else if (newName == file.OriginalName)
                    file.SetResult(newName, file.JustRenamed ? FileState.Renamed : FileState.Unchanged,
                        file.JustRenamed ? "Umbenannt ✓" : "Name ist bereits korrekt");
                else
                    file.SetResult(newName, FileState.Ready, "Bereit");
            }
        }

        // Zwei Dateien dürfen im selben Ordner nicht denselben Namen bekommen
        var duplicates = _files
            .Where(f => f.State == FileState.Ready)
            .GroupBy(f => Path.Combine(f.Folder, f.NewName), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var file in duplicates)
            file.SetResult(file.NewName, FileState.Error, "Zielname kommt mehrfach vor");

        UpdateStatusText();
    }

    // ---------- Umbenennen ----------

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var todo = _files.Where(f => f.State == FileState.Ready).ToList();
        if (todo.Count == 0)
        {
            MessageBox.Show(this, "Es gibt keine Dateien, die bereit zum Umbenennen sind.", "Umbenennen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"{todo.Count} Datei(en) jetzt umbenennen?", "Umbenennen",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        _lastRenames.Clear();
        int failed = 0;

        foreach (var file in todo)
        {
            var source = file.FullPath;
            var target = Path.Combine(file.Folder, file.NewName);
            try
            {
                // Reine Groß-/Kleinschreibungsänderung ist erlaubt, sonst nichts überschreiben
                if (File.Exists(target) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Zieldatei existiert bereits");

                File.Move(source, target);
                file.FullPath = target;
                file.JustRenamed = true;
                _lastRenames.Add((file, source, target));
            }
            catch (Exception ex)
            {
                file.LookupError = $"Umbenennen fehlgeschlagen: {ex.Message}";
                failed++;
            }
        }

        UndoButton.IsEnabled = _lastRenames.Count > 0;
        RefreshNames();

        if (failed > 0)
            MessageBox.Show(this, $"{_lastRenames.Count} umbenannt, {failed} fehlgeschlagen (siehe rote Einträge).",
                "Umbenennen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        int failed = 0;
        foreach (var (file, from, to) in Enumerable.Reverse(_lastRenames))
        {
            try
            {
                File.Move(to, from);
                file.FullPath = from;
                file.JustRenamed = false;
            }
            catch (Exception ex)
            {
                file.LookupError = $"Rückgängig fehlgeschlagen: {ex.Message}";
                failed++;
            }
        }

        _lastRenames.Clear();
        UndoButton.IsEnabled = false;
        RefreshNames();

        if (failed > 0)
            MessageBox.Show(this, $"{failed} Datei(en) konnten nicht zurückbenannt werden.", "Rückgängig",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------- Liste ----------

    private List<MediaFile> SelectedFiles() => LeftList.SelectedItems.Cast<MediaFile>().ToList();

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
            RemoveSelected_Click(sender, e);
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (var file in SelectedFiles())
            _files.Remove(file);
        RefreshNames();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _files.Clear();
        _lastRenames.Clear();
        UndoButton.IsEnabled = false;
        RefreshNames();
    }

    /// <summary>Hält die Markierung in beiden Listen gleich.</summary>
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;

        _syncingSelection = true;
        var source = (ListBox)sender;
        var other = source == LeftList ? RightList : LeftList;
        other.SelectedItems.Clear();
        foreach (var item in source.SelectedItems)
            other.SelectedItems.Add(item);
        _syncingSelection = false;
    }

    /// <summary>Scrollt die beiden Listen gemeinsam, damit links und rechts immer zusammenpassen.</summary>
    private void HookScrollSync()
    {
        _leftScroll = FindChild<ScrollViewer>(LeftList);
        _rightScroll = FindChild<ScrollViewer>(RightList);
        if (_leftScroll is null || _rightScroll is null)
            return;

        _leftScroll.ScrollChanged += (_, e) => SyncScroll(_rightScroll, e);
        _rightScroll.ScrollChanged += (_, e) => SyncScroll(_leftScroll, e);
    }

    private void SyncScroll(ScrollViewer target, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || e.VerticalChange == 0)
            return;
        _syncingScroll = true;
        target.ScrollToVerticalOffset(e.VerticalOffset);
        _syncingScroll = false;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    // ---------- Status ----------

    private void SetBusy(bool busy, string? message = null)
    {
        MatchButton.IsEnabled = !busy;
        RenameButton.IsEnabled = !busy;
        AssignSeriesButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;
        if (busy && message is not null)
            StatusText.Text = message;
        else
            UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (_files.Count == 0)
        {
            StatusText.Text = "Keine Dateien";
            return;
        }

        int ready = _files.Count(f => f.State == FileState.Ready);
        int errors = _files.Count(f => f.State == FileState.Error);
        int pending = _files.Count(f => f.State == FileState.Pending);
        StatusText.Text = $"{_files.Count} Dateien  ·  {ready} bereit  ·  {errors} Fehler" +
                          (pending > 0 ? $"  ·  {pending} nicht abgeglichen" : "");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.ApiKey = ApiKeyBox.Password.Trim();
        _settings.Language = LanguageBox.Text.Trim();
        _settings.Pattern = PatternBox.Text;
        try
        {
            _settings.Save();
        }
        catch (Exception)
        {
            // Einstellungen nicht speichern zu können soll das Schließen nicht verhindern
        }
        _client?.Dispose();
    }
}
