using Avalonia.Controls;

namespace Playout.App;

/// <summary>What the pause dialog returns: what to talk about, how long (0 = until Next), and whether it plays next.</summary>
public sealed record PauseRequest(string Comment, double Seconds, bool PlayNext);

/// <summary>Asks for a pause to put in the playlist, or changes one (<paramref name="editing"/>).</summary>
public partial class PauseDialog : Window
{
    public PauseDialog() : this(null) { }

    public PauseDialog(PauseRequest? editing)
    {
        InitializeComponent();
        Localization.Apply(this);
        LengthBox.GotFocus += (_, _) => ForRadio.IsChecked = true;
        CancelButton.Click += (_, _) => Close(null);
        AddButton.Click += (_, _) => Add();
        Opened += (_, _) => CommentBox.Focus();
        if (editing is { } e)
        {
            Title = L.T("Edit pause");
            AddButton.Content = L.T("Save");
            CommentBox.Text = e.Comment;
            if (e.Seconds > 0) LengthBox.Text = Format.Duration(e.Seconds);
            else UntilNextRadio.IsChecked = true;
            PlayNextBox.IsVisible = false;
        }
    }

    void Add()
    {
        double seconds = 0;
        if (ForRadio.IsChecked == true)
        {
            if (Format.ParseCueTime(LengthBox.Text) is not { } length || length < 1)
            {
                ErrorText.Text = L.T("Enter how long the pause is, e.g. 1:30 for a minute and a half");
                ErrorText.IsVisible = true;
                return;
            }
            seconds = length;
        }
        string comment = CommentBox.Text?.Trim() is { Length: > 0 } c ? c : L.T("Pause");
        Close(new PauseRequest(comment, seconds, PlayNextBox.IsChecked == true));
    }
}
