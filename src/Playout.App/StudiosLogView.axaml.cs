using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>A line of a studio's log as listed: a problem in red.</summary>
public sealed record LogRow(string Time, string Text, IBrush Brush);

/// <summary>
/// Sintoniza Pro → Station → Studios: what each computer on air wrote in its log, a day at a time (sent by
/// <see cref="LogShipper"/>), the problems in red; only the problems, or a search; saved as a file to send on.
/// </summary>
public partial class StudiosLogView : UserControl
{
    static readonly IBrush Problem = new SolidColorBrush(Color.Parse("#FF7A7E"));
    static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#D6DAE2"));

    readonly LibraryDb? _db;
    List<StudioLogLine> _lines = new();
    int _loadVersion;
    bool _loading;

    // Parameterless constructor for the XAML previewer.
    public StudiosLogView() => InitializeComponent();

    public StudiosLogView(LibraryDb db, string? computer = null)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        DayBox.SelectedDate = StationTime.Today;
        DayBox.SelectedDateChanged += async (_, _) => await Reload();
        ComputerBox.SelectionChanged += async (_, _) => { if (!_loading) await Reload(); };
        BackButton.Click += (_, _) => DayBox.SelectedDate = Day.AddDays(-1);
        ForwardButton.Click += (_, _) => DayBox.SelectedDate = Day.AddDays(1);
        TodayButton.Click += (_, _) => DayBox.SelectedDate = StationTime.Today;
        ProblemsBox.IsCheckedChanged += (_, _) => Show();
        SearchBox.TextChanged += (_, _) => Show();
        ReloadButton.Click += async (_, _) => await Reload(computers: true);
        SaveButton.Click += async (_, _) => await SaveAs();
        _wanted = computer;
        AttachedToVisualTree += async (_, _) => await Reload(computers: true);
    }

    string? _wanted;
    DateTime Day => (DayBox.SelectedDate ?? StationTime.Today).Date;
    string? Computer => ComputerBox.SelectedItem as string;

    internal async Task Reload(bool computers = false)
    {
        if (_db is not { } db) return;
        int version = ++_loadVersion;
        var day = Day;
        StatusText.Text = L.T("Reading…");
        try
        {
            if (computers || ComputerBox.ItemCount == 0)
            {
                var list = await Task.Run(() => db.StudioLogComputers(StationStore.Station));
                if (version != _loadVersion) return;
                _loading = true;
                try
                {
                    string? keep = _wanted ?? Computer;
                    ComputerBox.ItemsSource = list;
                    ComputerBox.SelectedIndex = Math.Max(0, list.IndexOf(keep ?? ""));
                }
                finally { _loading = false; }
                _wanted = null;
            }
            if (Computer is not { } computer)
            {
                _lines = new();
                StatusText.Text = L.T("No computer has sent its log yet (the computers on air send it once they have this version).");
                Show();
                return;
            }
            var lines = await Task.Run(() => db.StudioLog(StationStore.Station, computer, day));
            if (version != _loadVersion) return;
            _lines = lines;
            Show();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion) StatusText.Text = L.F("Can't read the studios' logs: {0}", ex.Message);
        }
    }

    /// <summary>The lines read, as the filters say.</summary>
    List<StudioLogLine> Shown()
    {
        string search = SearchBox.Text?.Trim() ?? "";
        return _lines.Where(l => (ProblemsBox.IsChecked != true || l.Problem)
                                 && (search.Length == 0 || l.Line.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    void Show()
    {
        var shown = Shown();
        LineList.ItemsSource = shown.Select(l => new LogRow(l.At.ToString("HH:mm:ss"), l.Line, l.Problem ? Problem : Normal)).ToList();
        if (Computer == null) return;
        int problems = _lines.Count(l => l.Problem);
        StatusText.Text = L.F("{0} lines, {1} problems", _lines.Count, problems) + (shown.Count != _lines.Count ? " · " + L.F("{0} shown", shown.Count) : "");
        SaveButton.IsEnabled = shown.Count > 0;
    }

    async Task SaveAs()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || Computer is not { } computer) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Save as file…"),
            SuggestedFileName = $"{computer}-{Day:yyyy-MM-dd}.log",
            FileTypeChoices = [new FilePickerFileType(L.T("Text")) { Patterns = ["*.log", "*.txt"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllLinesAsync(path, Shown().Select(l => $"{l.At:HH:mm:ss}  {l.Line}"));
            StatusText.Text = L.F("Saved: {0}", path);
        }
        catch (Exception ex) { StatusText.Text = L.F("Not saved: {0}", ex.Message); }
    }
}
