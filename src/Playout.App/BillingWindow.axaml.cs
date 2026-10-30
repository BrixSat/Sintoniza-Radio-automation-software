using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Playout.Library;

namespace Playout.App;

/// <summary>A line of the billing summary as shown.</summary>
public sealed class BillingRow(BillingLine line)
{
    public string Client => line.Client;
    public string Sector => line.Sector;
    public string Campaign => line.Campaign.Length > 0 ? line.Campaign : L.T("(no campaign)");
    public string Booked => $"{line.Booked}";
    public string Aired => $"{line.Aired}";
    public string Missed => line.Missed > 0 ? $"{line.Missed}" : "";
    public IBrush MissedBrush { get; } = new SolidColorBrush(Color.Parse("#E5484D"));
    public string NotBooked => line.NotBooked > 0 ? $"{line.NotBooked}" : "";
    public string Time => Format.Duration(line.Seconds);
}

/// <summary>What to invoice over a period: every client's campaigns, from the schedule and the as-run log (Sintoniza Pro → Clients → Billing…).</summary>
public partial class BillingWindow : Window
{
    readonly ScheduleData _data;
    readonly LibraryDb? _db;
    readonly string _station;
    List<BillingLine> _lines = new();
    int _version;

    public BillingWindow() : this(new ScheduleData(), null, "") { }

    public BillingWindow(ScheduleData data, LibraryDb? db, string station)
    {
        InitializeComponent();
        Localization.Apply(this);
        _data = data;
        _db = db;
        _station = station;
        SetMonth(-1); // invoices are made for the month that ended
        FromBox.SelectedDateChanged += async (_, _) => await Reload();
        ToBox.SelectedDateChanged += async (_, _) => await Reload();
        LastMonthButton.Click += async (_, _) => { SetMonth(-1); await Reload(); };
        ThisMonthButton.Click += async (_, _) => { SetMonth(0); await Reload(); };
        CsvButton.Click += async (_, _) => await Save(csv: true);
        HtmlButton.Click += async (_, _) => await Save(csv: false);
        CloseButton.Click += (_, _) => Close();
        Opened += async (_, _) => await Reload();
    }

    /// <summary>A month: 0 this one (to today), -1 the one before.</summary>
    void SetMonth(int offset)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(offset);
        FromBox.SelectedDate = first;
        ToBox.SelectedDate = offset == 0 ? DateTime.Today : first.AddMonths(1).AddDays(-1);
    }

    (DateTime From, DateTime To) Period()
    {
        var (from, to) = (FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today);
        return to < from ? (to.Date, from.Date) : (from.Date, to.Date);
    }

    async Task Reload()
    {
        var (from, to) = Period();
        int version = ++_version;
        SummaryText.Text = L.T("Reading…");
        try
        {
            var db = _db;
            var data = _data;
            var lines = await Task.Run(() => Billing.Build(data, AsRunDb.ReadAll(db, AsRunLog.DefaultFolder, from, to), from, to, DateTime.Now));
            if (version != _version) return;
            _lines = lines;
            LineList.ItemsSource = _lines.Select(l => new BillingRow(l)).ToList();
            SummaryText.Text = _lines.Count == 0 ? L.T("No adverts booked nor on air in these days.") : Billing.Summary(_lines);
            CsvButton.IsEnabled = HtmlButton.IsEnabled = _lines.Count > 0;
        }
        catch (Exception ex)
        {
            SummaryText.Text = L.F("The as-run log can't be read: {0}", ex.Message);
        }
    }

    async Task Save(bool csv)
    {
        if (_lines.Count == 0) return;
        var (from, to) = Period();
        var report = Billing.ToReport(_lines, _station, from, to);
        string days = from == to ? $"{from:yyyy-MM-dd}" : $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
        string name = string.Concat($"{L.T("invoicing")}-{days}".Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? L.T("Save the report as CSV") : L.T("Save the report as a page to print"),
            SuggestedFileName = $"{name}.{(csv ? "csv" : "html")}",
            DefaultExtension = csv ? "csv" : "html",
            FileTypeChoices = [csv ? new FilePickerFileType(L.T("CSV")) { Patterns = ["*.csv"] } : new FilePickerFileType(L.T("Web page")) { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            if (csv) await File.WriteAllTextAsync(path, report.ToCsv(';'), new UTF8Encoding(true));
            else
            {
                await File.WriteAllTextAsync(path, report.ToHtml(L.F("Made by {0} on {1:yyyy-MM-dd HH:mm}", AppInfo.ScheduleName, DateTime.Now)), new UTF8Encoding(false));
                Updates.OpenInBrowser(path);
            }
            SummaryText.Text = L.F("Saved {0}", Path.GetFileName(path)) + " · " + Billing.Summary(_lines);
        }
        catch (Exception ex)
        {
            SummaryText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }
}
