using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Playout.App;

/// <summary>
/// A window that can also be a page of another window (Sintoniza Pro shows Categories, Station settings, Cartwalls and
/// Import music in its right-hand area): <see cref="Embed"/> moves its content, styles and resources into a host
/// control, and from then on its dialogs, file pickers and "close" go through the window it is shown in.
/// </summary>
public class PageWindow : Window
{
    Control? _page;

    /// <summary>Shown as a page (not as a window of its own).</summary>
    public bool IsPage => _page != null;

    /// <summary>As a page: it finished (saved, or cancelled with false) — the host shows it afresh.</summary>
    public event Action<object?>? Finished;

    /// <summary>Its content as a page, with its styles and resources; its "opened" work runs now.</summary>
    public Control Embed()
    {
        var host = new Border();
        foreach (var style in Styles.ToList())
        {
            Styles.Remove(style);
            host.Styles.Add(style);
        }
        foreach (var (key, value) in Resources.ToList())
            host.Resources[key] = value;
        var content = Content as Control;
        Content = null;
        host.Child = content;
        _page = host;
        OnOpened(EventArgs.Empty); // what it does when it opens (loading its lists…)
        return host;
    }

    /// <summary>As a page that is put away: what it does when it closes (stops sound, lets go of files).</summary>
    public void Discard()
    {
        if (_page != null) OnClosed(EventArgs.Empty);
    }

    /// <summary>The window it is shown in: owner of its dialogs.</summary>
    protected Window DialogOwner => _page != null && TopLevel.GetTopLevel(_page) is Window w ? w : this;

    /// <summary>File and folder pickers of the window it is shown in.</summary>
    protected IStorageProvider Storage => _page != null && TopLevel.GetTopLevel(_page) is { } top ? top.StorageProvider : StorageProvider;

    /// <summary>Closes the window, or as a page tells its host it finished.</summary>
    protected void Finish(object? result = null)
    {
        if (_page != null) Finished?.Invoke(result);
        else Close(result);
    }
}
