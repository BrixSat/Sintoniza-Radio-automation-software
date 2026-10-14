using Avalonia.Controls;
using Playout.Engine;

namespace Playout.App;

/// <summary>What the stream dialog returns: another station's stream, how long to play it (0 = until Stop or Next), and where it goes.</summary>
public sealed record StreamRequest(string Url, string Name, double Seconds, bool PlayNext);

/// <summary>Asks for an internet stream to put in the playlist.</summary>
public partial class StreamDialog : Window
{
    public StreamDialog()
    {
        InitializeComponent();
        LengthBox.GotFocus += (_, _) => ForRadio.IsChecked = true;
        CancelButton.Click += (_, _) => Close(null);
        AddButton.Click += (_, _) => Add();
        Opened += (_, _) => NameBox.Focus();
    }

    void Add()
    {
        string url = UrlBox.Text?.Trim() ?? "";
        if (!PlayoutEngine.IsUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ShowError("Enter the stream address, starting with http:// or https://");
            return;
        }

        double seconds = 0;
        if (ForRadio.IsChecked == true)
        {
            if (Format.ParseCueTime(LengthBox.Text) is not { } length || length < 1)
            {
                ShowError("Enter how long to play it, e.g. 1:00:00 for an hour or 30:00 for half an hour");
                return;
            }
            seconds = length;
        }

        string name = NameBox.Text?.Trim() is { Length: > 0 } n ? n : uri.Host;
        Close(new StreamRequest(url, name, seconds, PlayNextBox.IsChecked == true));
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
