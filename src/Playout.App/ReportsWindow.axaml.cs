using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Playout.Library;

namespace Playout.App;

/// <summary>Music reports for royalties and proof of broadcast for advertisers, made from the as-run log.</summary>
public partial class ReportsWindow : Window
{
    const int PreviewRows = 300;
    static readonly string AllAdvertisers = L.T("All advertisers");

    readonly string _folder;
    readonly string _station;
    List<AsRunEntry> _entries = new();
    Report? _report;
    int _loadVersion;

    // Parameterless constructor for the XAML previewer.
    public ReportsWindow() : this(AsRunLog.DefaultFolder, "") { }

    /// <summary>The library database the as-run log is read from (with this computer's files for what it lacks).</summary>
    internal Func<LibraryDb?> Database { get; init; } = () => null;

    public ReportsWindow(string folder, string station)
    {
        InitializeComponent();
        Localization.Apply(this);
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
        SummaryText.Text = L.T("Reading…");
        var entries = await Task.Run(() => AsRunDb.ReadAll(Database(), _folder, from, to));
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
        SummaryText.Text = L.F("{0}: {1}", _report.Period, _report.Summary);
        PreviewText.Text = _report.Rows.Count == 0 ? L.T("Nothing went on air in these days (or nothing of this kind).") : Table(_report);
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
        if (report.Rows.Count > PreviewRows) text.AppendLine(L.F("… and {0} more (all of them are saved)", report.Rows.Count - PreviewRows));
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
        if (Kind == ReportKind.Adverts && AdvertiserBox.SelectedItem is string a && a != AllAdvertisers) name += "-" + string.Concat(a.Split(Path.GetInvalidFileNameChars()));
        var (from, to) = (FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today);
        if (to < from) (from, to) = (to, from);
        string days = from.Date == to.Date ? $"{from:yyyy-MM-dd}" : $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? L.T("Save the report as CSV") : L.T("Save the report as a page to print"),
            SuggestedFileName = $"{name}-{days}.{(csv ? "csv" : "html")}",
            DefaultExtension = csv ? "csv" : "html",
            FileTypeChoices = [csv ? new FilePickerFileType(L.T("CSV")) { Patterns = ["*.csv"] } : new FilePickerFileType(L.T("Web page")) { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            if (csv) await File.WriteAllTextAsync(path, report.ToCsv(SeparatorBox.SelectedIndex == 1 ? ';' : ','), new UTF8Encoding(true));
            else await File.WriteAllTextAsync(path, report.ToHtml(L.F("Made by {0} on {1:yyyy-MM-dd HH:mm}", AppInfo.Name, DateTime.Now)), new UTF8Encoding(false));
            SummaryText.Text = L.F("Saved {0}", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            SummaryText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }
}
