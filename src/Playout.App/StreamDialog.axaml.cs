using Avalonia.Controls;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// What the stream dialog returns: another station's stream, how long to play it (0 = until Stop or Next), and where it
/// goes: next, or (in a day's playlist) at a time, on the dot or after the item on air.
/// </summary>
public sealed record StreamRequest(string Url, string Name, double Seconds, bool PlayNext, DateTime? At = null, bool Hard = true);

/// <summary>Asks for an internet stream to put in the playlist.</summary>
public partial class StreamDialog : Window
{
    readonly DateTime? _hour;

    public StreamDialog() : this(null) { }

    /// <summary>With <paramref name="hour"/>: for a day's playlist, starting at a time in that hour.</summary>
    public StreamDialog(DateTime? hour, DateTime? at = null)
    {
        _hour = hour;
        InitializeComponent();
        Localization.Apply(this);
        LengthBox.GotFocus += (_, _) => ForRadio.IsChecked = true;
        CancelButton.Click += (_, _) => Close(null);
        AddButton.Click += (_, _) => Add();
        Opened += (_, _) => NameBox.Focus();
        if (hour is { } h)
        {
            Title = L.T("Stream in the playlist");
            AddButton.Content = L.T("Put in");
            PlayNextBox.IsVisible = false;
            WhenPanel.IsVisible = true;
            AtBox.Text = (at ?? h).ToString("HH:mm:ss");
            DayText.Text = L.F("on {0:dddd dd/MM}", h);
            ForRadio.IsChecked = true; // nobody may be there to press Stop
            LengthBox.Text = "1:00:00";
        }
    }

    void Add()
    {
        string url = UrlBox.Text?.Trim() ?? "";
        if (!PlayoutEngine.IsUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ShowError(L.T("Enter the stream address, starting with http:// or https://"));
            return;
        }

        double seconds = 0;
        if (ForRadio.IsChecked == true)
        {
            if (Format.ParseCueTime(LengthBox.Text) is not { } length || length < 1)
            {
                ShowError(L.T("Enter how long to play it, e.g. 1:00:00 for an hour or 30:00 for half an hour"));
                return;
            }
            seconds = length;
        }

        DateTime? at = null;
        if (_hour is { } hour)
        {
            if (ScheduleTimes.Parse(AtBox.Text ?? "") is not { EveryHour: false } t || hour.Date + t.Time < hour || hour.Date + t.Time >= hour.AddHours(1))
            {
                ShowError(L.F("Enter when it starts, between {0:HH}:00:00 and {0:HH}:59:59", hour));
                return;
            }
            at = hour.Date + t.Time;
        }

        string name = NameBox.Text?.Trim() is { Length: > 0 } n ? n : uri.Host;
        Close(new StreamRequest(url, name, seconds, PlayNextBox.IsChecked == true, at, HardRadio.IsChecked == true));
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
