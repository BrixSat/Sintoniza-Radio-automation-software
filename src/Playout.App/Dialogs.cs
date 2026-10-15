using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Playout.App;

/// <summary>Small modal prompts (ask for a name, confirm a deletion).</summary>
static class Dialogs
{
    /// <summary>
    /// Asks for a line of text; null when cancelled. <paramref name="validate"/> returns an error to show
    /// (the dialog stays open) or null to accept the trimmed text.
    /// </summary>
    public static Task<string?> AskText(Window owner, string title, string prompt, string initial, Func<string, string?> validate)
    {
        var box = new TextBox { Text = initial };
        var error = new TextBlock { Foreground = (IBrush)owner.FindResource("OnAir")!, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var ok = new Button { Content = L.T("Save"), IsDefault = true };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };

        var window = Create(title, new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = prompt }, box, error, Buttons(cancel, ok) },
        });
        ok.Click += (_, _) =>
        {
            string text = box.Text?.Trim() ?? "";
            if (validate(text) is { } message)
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }
            window.Close(text);
        };
        cancel.Click += (_, _) => window.Close(null);
        window.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog<string?>(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string action)
    {
        var ok = new Button { Content = action, IsDefault = true };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        var window = Create(title, new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, Buttons(cancel, ok) },
        });
        ok.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);
        return await window.ShowDialog<bool?>(owner) == true;
    }

    static Window Create(string title, Control content) => new()
    {
        Title = title,
        Width = 380,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new Border { Padding = new Thickness(18), Child = content },
    };

    static StackPanel Buttons(Button cancel, Button ok)
    {
        foreach (var b in new[] { cancel, ok })
        {
            b.MinWidth = 90;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { cancel, ok },
        };
    }
}
