using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Station → Downloads: the files the computer on air fetches again and again (the news), kept in the
/// station's settings (<see cref="DownloadSettings"/>, run by <see cref="Downloader"/>). Test fetches one here, to a
/// file of its own, without putting it in place.
/// </summary>
public partial class DownloadsView : UserControl
{
    static readonly DownloadSource[] Sources = [DownloadSource.Page, DownloadSource.Link, DownloadSource.Ftp];

    readonly StationStore? _store;
    readonly Func<StationSettings>? _fallback;
    readonly string _who = "";
    List<DownloadSettings> _items = new();
    long _version;
    DownloadSettings? _shown;
    bool _filling;

    // Parameterless constructor for the XAML previewer.
    public DownloadsView() => InitializeComponent();

    /// <param name="fallback">The station's settings when none were saved yet (this computer's).</param>
    public DownloadsView(StationStore store, string who, Func<StationSettings> fallback)
    {
        InitializeComponent();
        Localization.Apply(this);
        _store = store;
        _fallback = fallback;
        _who = who;
        List.SelectionChanged += (_, _) => Show(List.SelectedIndex >= 0 && List.SelectedIndex < _items.Count ? _items[List.SelectedIndex] : null);
        AddButton.Click += (_, _) =>
        {
            Keep();
            _items.Add(new DownloadSettings { Name = L.T("News") });
            ShowList(_items.Count - 1);
        };
        RemoveButton.Click += (_, _) =>
        {
            if (List.SelectedIndex < 0 || List.SelectedIndex >= _items.Count) return;
            _shown = null;
            _items.RemoveAt(List.SelectedIndex);
            ShowList(Math.Min(List.SelectedIndex, _items.Count - 1));
        };
        SourceBox.SelectionChanged += (_, _) => ShowSource();
        EveryRadio.IsCheckedChanged += (_, _) => ShowWhen();
        foreach (var box in new[] { NameBox, SaveAsBox })
            box.TextChanged += (_, _) => { if (!_filling) { Keep(); ShowList(List.SelectedIndex, quiet: true); } };
        SaveAsButton.Click += async (_, _) => await ChooseFile();
        TestButton.Click += async (_, _) => await Test();
        SaveButton.Click += async (_, _) => await Save();
        UndoButton.Click += async (_, _) => await Load();
        AttachedToVisualTree += async (_, _) => { if (_version == 0 && _items.Count == 0) await Load(); };
    }

    async Task Load()
    {
        if (_store is not { } store) return;
        try
        {
            var (settings, version) = await Task.Run(store.Load);
            _version = version;
            _items = (settings?.Downloads ?? new()).Select(d => d.Clone()).ToList();
            _shown = null;
            ShowList(_items.Count > 0 ? 0 : -1);
            StatusText.Text = "";
        }
        catch (Exception ex) { StatusText.Text = L.F("The station's settings can't be read: {0}", ex.Message); }
    }

    void ShowList(int select, bool quiet = false)
    {
        var lines = _items.Select(Line).ToList();
        if (quiet && List.ItemCount == lines.Count)
        {
            // Only the names: the selection (and what is being typed) stays.
            _filling = true;
            int at = List.SelectedIndex;
            List.ItemsSource = lines;
            List.SelectedIndex = at;
            _filling = false;
            return;
        }
        List.ItemsSource = lines;
        List.SelectedIndex = select;
        if (select < 0) Show(null);
    }

    static string Line(DownloadSettings d)
    {
        string name = d.Name.Trim().Length > 0 ? d.Name.Trim() : L.T("(no name)");
        string when = d.EveryMinutes > 0 ? L.F("every {0} min", d.EveryMinutes) : L.F("at {0} past the hour", d.AtMinutes.Trim());
        return (d.Enabled ? "" : L.T("(off) ")) + $"{name} · {when}" + (d.SaveAs.Trim().Length > 0 ? $" → {Path.GetFileName(d.SaveAs.Trim())}" : "");
    }

    void Show(DownloadSettings? d)
    {
        if (_filling) return;
        Keep();
        _shown = d;
        Editor.IsEnabled = d != null;
        RemoveButton.IsEnabled = d != null;
        _filling = true;
        var shown = d ?? new DownloadSettings();
        EnabledBox.IsChecked = shown.Enabled;
        NameBox.Text = shown.Name;
        SourceBox.SelectedIndex = Math.Max(0, Array.IndexOf(Sources, shown.Source));
        AddressBox.Text = shown.Address;
        PatternBox.Text = shown.Pattern;
        UserBox.Text = shown.User;
        PasswordBox.Text = shown.Password;
        SaveAsBox.Text = shown.SaveAs;
        EveryRadio.IsChecked = shown.EveryMinutes > 0;
        AtRadio.IsChecked = shown.EveryMinutes <= 0;
        EveryBox.Value = shown.EveryMinutes > 0 ? shown.EveryMinutes : 15;
        AtBox.Text = shown.AtMinutes;
        TooOldBox.Value = shown.TooOldMinutes;
        ShortestBox.Value = shown.ShortestSeconds;
        _filling = false;
        TestText.Text = "";
        ShowSource();
        ShowWhen();
        _ = ShowFile(shown.SaveAs);
    }

    /// <summary>What the editor shows, back into the download shown.</summary>
    void Keep()
    {
        if (_shown is not { } d || _filling) return;
        d.Enabled = EnabledBox.IsChecked == true;
        d.Name = NameBox.Text?.Trim() ?? "";
        d.Source = Sources[Math.Max(0, SourceBox.SelectedIndex)];
        d.Address = AddressBox.Text?.Trim() ?? "";
        d.Pattern = PatternBox.Text?.Trim() is { Length: > 0 } p ? p : DownloadSettings.DefaultPattern;
        d.User = UserBox.Text?.Trim() ?? "";
        d.Password = PasswordBox.Text ?? "";
        d.SaveAs = SaveAsBox.Text?.Trim() is { Length: > 0 } saveAs ? SharedPaths.ToLibrary(saveAs) : "";
        d.EveryMinutes = EveryRadio.IsChecked == true ? (int)(EveryBox.Value ?? 15) : 0;
        d.AtMinutes = AtBox.Text?.Trim() ?? "";
        d.TooOldMinutes = (int)(TooOldBox.Value ?? 0);
        d.ShortestSeconds = (int)(ShortestBox.Value ?? 0);
    }

    void ShowSource()
    {
        var source = Sources[Math.Max(0, SourceBox.SelectedIndex)];
        PatternPanel.IsVisible = source == DownloadSource.Page;
        AddressLabel.Text = source switch
        {
            DownloadSource.Page => L.T("The page's address"),
            DownloadSource.Ftp => L.T("The file on the FTP server (ftp://server/folder/file.mp3)"),
            _ => L.T("The file's address"),
        };
    }

    void ShowWhen()
    {
        EveryBox.IsEnabled = EveryRadio.IsChecked == true;
        AtBox.IsEnabled = EveryRadio.IsChecked != true;
    }

    async Task ShowFile(string saveAs)
    {
        FileText.Text = "";
        if (saveAs.Trim().Length == 0) return;
        var (exists, at) = await Task.Run(() =>
        {
            try
            {
                string local = SharedPaths.ToLocal(saveAs.Trim());
                return File.Exists(local) ? (true, File.GetLastWriteTimeUtc(local)) : (false, default);
            }
            catch (Exception) { return (false, default(DateTime)); }
        });
        if (_shown?.SaveAs != saveAs) return;
        FileText.Text = exists ? L.F("The file here: fetched {0:dd/MM/yyyy HH:mm}.", StationTime.FromUtc(at)) : L.T("The file isn't there yet (or this computer doesn't reach it).");
    }

    async Task ChooseFile()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Where the download is saved"),
            SuggestedFileName = Path.GetFileName(SaveAsBox.Text?.Trim() is { Length: > 0 } s ? s : "news.mp3"),
            DefaultExtension = "mp3",
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        SaveAsBox.Text = SharedPaths.ToLibrary(path);
    }

    async Task Test()
    {
        Keep();
        if (_shown is not { } d) return;
        TestButton.IsEnabled = false;
        TestText.Text = L.T("Fetching…");
        string extension = Path.GetExtension(d.SaveAs.Trim()) is { Length: > 0 } e ? e : ".mp3";
        string temp = Path.Combine(Path.GetTempPath(), $"sintoniza-download-{Guid.NewGuid():N}{extension}");
        try
        {
            var got = await Download.FetchAsync(d.Clone(), temp);
            TestText.Text = L.F("Found {0}: it plays {1}. Not put in place: the computer on air does that.", got.Url, Format.Duration(got.Seconds));
        }
        catch (Exception ex) { TestText.Text = L.F("✗ {0}", ex.GetBaseException().Message); }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception) { /* a temporary file */ }
            TestButton.IsEnabled = true;
        }
    }

    async Task Save()
    {
        Keep();
        if (_store is not { } store) return;
        foreach (var d in _items.Where(d => d.Enabled))
        {
            string name = d.Name.Trim().Length > 0 ? d.Name.Trim() : L.T("(no name)");
            if (d.Address.Trim().Length == 0 || d.SaveAs.Trim().Length == 0)
            {
                StatusText.Text = L.F("{0}: give it an address and where it is saved.", name);
                return;
            }
            // Rooted here, or as a Windows studio has it (a drive, a share) when this computer is another kind.
            if (!Path.IsPathRooted(SharedPaths.ToLocal(d.SaveAs)) && !System.Text.RegularExpressions.Regex.IsMatch(d.SaveAs, @"^([A-Za-z]:[\/]|\\)"))
            {
                StatusText.Text = L.F("{0}: where it is saved must be a whole path (Choose…).", name);
                return;
            }
            if (d.EveryMinutes <= 0 && (d.Minutes().Count == 0 || !d.MinutesAreRight()))
            {
                StatusText.Text = L.F("{0}: say at which minutes past the hour (0 to 59, e.g. 2, 32).", name);
                return;
            }
            if (d.TooOldMinutes > 0 && d.TooOldMinutes < d.IntervalMinutes() * 1.5)
            {
                StatusText.Text = L.F("{0}: too old after {1} minutes, but it is fetched only every {2}: give it at least {3}.", name, d.TooOldMinutes,
                    d.IntervalMinutes(), (int)Math.Ceiling(d.IntervalMinutes() * 1.5));
                return;
            }
        }
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        if (_items.Where(d => d.Enabled).GroupBy(d => d.SaveAs, comparer).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            StatusText.Text = L.F("Two downloads are saved as {0}: one file each.", Path.GetFileName(twice.Key));
            return;
        }
        var items = _items.Select(d => d.Clone()).ToList();
        string? problem = await Task.Run(() =>
        {
            try
            {
                var (settings, version) = store.Load();
                if (version != _version) return L.T("They were saved on another computer meanwhile: Undo changes, and make yours again.");
                // Never saved yet: this computer's, as the other pages do (never blank ones for every studio).
                var updated = settings?.Clone() ?? _fallback?.Invoke() ?? new StationSettings();
                updated.Downloads = items;
                _version = store.Save(updated, version, _who);
                return null;
            }
            catch (ScheduleStore.ChangedException) { return L.T("They were saved on another computer meanwhile: Undo changes, and make yours again."); }
            catch (Exception ex) { return ex.Message; }
        });
        StatusText.Text = problem ?? L.F("Downloads saved at {0:HH:mm:ss}. The computer on air takes them within a minute.", StationTime.Now);
    }
}
