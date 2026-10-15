using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Playout.App;

/// <summary>Music reports for royalties and proof of broadcast for advertisers, made from the as-run log.</summary>
public partial class ReportsWindow : Window
{
    const int PreviewRows = 300;
    const string AllAdvertisers = "All advertisers";

    readonly string _folder;
    readonly string _station;
    List<AsRunEntry> _entries = new();
    Report? _report;
    int _loadVersion;

    // Parameterless constructor for the XAML previewer.
    public ReportsWindow() : this(AsRunLog.DefaultFolder, "") { }

    public ReportsWindow(string folder, string station)
    {
        InitializeComponent();
        _folder = folder;
        _station = station;
        // This month so far.
        FromBox.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToBox.SelectedDate = DateTime.Today;

        KindBox.SelectionChanged += async (_, _) => await Reload();
        FromBox.SelectedDateChanged += async (_, _) => await Reload();
        ToBox.SelectedDateChanged += async (_, _) => await Reload();
        AdvertiserBox.SelectionChanged += (_, _) => ShowReport();
        CsvButton.Click += async (_, _) => await Save(csv: true);
        HtmlButton.Click += async (_, _) => await Save(csv: false);
        CloseButton.Click += (_, _) => Close();
        Opened += async (_, _) => await Reload();
    }

    ReportKind Kind => (ReportKind)Math.Max(0, KindBox.SelectedIndex);

    /// <summary>Reads the as-run log for the chosen days (in the background: a year of it is a lot of lines).</summary>
    async Task Reload()
    {
        if (FromBox.SelectedDate is not { } from || ToBox.SelectedDate is not { } to) return;
        if (to < from) (from, to) = (to, from);
        int version = ++_loadVersion;
        SummaryText.Text = "Reading…";
        var entries = await Task.Run(() => AsRunLog.Read(_folder, from, to));
        if (version != _loadVersion) return;
        _entries = entries;

        var advertisers = entries.Where(e => e.Type == "Advert" && e.Advertiser.Length > 0).Select(e => e.Advertiser)
            .Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        var keep = AdvertiserBox.SelectedItem as string;
        AdvertiserBox.ItemsSource = new[] { AllAdvertisers }.Concat(advertisers).ToList();
        AdvertiserBox.SelectedItem = keep != null && advertisers.Contains(keep) ? keep : AllAdvertisers;
        AdvertiserPanel.IsVisible = Kind == ReportKind.Adverts;
        ShowReport();
    }

    void ShowReport()
    {
        if (FromBox.SelectedDate is not { } from || ToBox.SelectedDate is not { } to) return;
        if (to < from) (from, to) = (to, from);
        string? advertiser = Kind == ReportKind.Adverts && AdvertiserBox.SelectedItem is string a && a != AllAdvertisers ? a : null;
        _report = Report.Build(Kind, _entries, from, to, _station, advertiser);
        SummaryText.Text = $"{_report.Period}: {_report.Summary}";
        PreviewText.Text = _report.Rows.Count == 0 ? "Nothing went on air in these days (or nothing of this kind)." : Table(_report);
        CsvButton.IsEnabled = HtmlButton.IsEnabled = _report.Rows.Count > 0;
    }

    /// <summary>The first rows as a text table, columns lined up.</summary>
    static string Table(Report report)
    {
        var rows = report.Rows.Take(PreviewRows).ToList();
        var widths = report.Columns.Select((c, i) => Math.Min(40, Math.Max(c.Length, rows.Select(r => r[i].Length).DefaultIfEmpty(0).Max()))).ToArray();
        string Line(IReadOnlyList<string> fields) =>
            string.Join("  ", fields.Select((f, i) => (f.Length > widths[i] ? f[..(widths[i] - 1)] + "…" : f).PadRight(widths[i]))).TrimEnd();
        var text = new StringBuilder();
        text.AppendLine(Line(report.Columns));
        foreach (var row in rows) text.AppendLine(Line(row));
        if (report.Rows.Count > PreviewRows) text.AppendLine($"… and {report.Rows.Count - PreviewRows} more (all of them are saved)");
        return text.ToString();
    }

    async Task Save(bool csv)
    {
        if (_report is not { } report) return;
        string name = Kind switch
        {
            ReportKind.MusicPlays => "music",
            ReportKind.MusicTotals => "music-totals",
            ReportKind.Adverts => "adverts",
            _ => "as-run",
        };
        if (report.Title.Contains(" for ") && AdvertiserBox.SelectedItem is string a) name += "-" + string.Concat(a.Split(Path.GetInvalidFileNameChars()));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? "Save the report as CSV" : "Save the report as a page to print",
            SuggestedFileName = $"{name}-{report.Period.Replace(" to ", "_")}.{(csv ? "csv" : "html")}",
            DefaultExtension = csv ? "csv" : "html",
            FileTypeChoices = [csv ? new FilePickerFileType("CSV") { Patterns = ["*.csv"] } : new FilePickerFileType("Web page") { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            if (csv) await File.WriteAllTextAsync(path, report.ToCsv(SeparatorBox.SelectedIndex == 1 ? ';' : ','), new UTF8Encoding(true));
            else await File.WriteAllTextAsync(path, report.ToHtml($"Made by {AppInfo.Name} on {DateTime.Now:yyyy-MM-dd HH:mm}"), new UTF8Encoding(false));
            SummaryText.Text = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"Not saved: {ex.Message}";
        }
    }
}
