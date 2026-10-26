using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro's cartwall editor: the station's cartwalls (every studio plays them), each with
/// <see cref="AppSettings.CartCount"/> carts: a file, a colour and an icon. Saved with the station's settings.
/// </summary>
public partial class CartwallsWindow : PageWindow
{
    readonly List<CartwallPage> _pages;
    readonly Func<List<CartwallPage>, string?> _save = _ => null;
    readonly AudioRouting _routing = new(new(), new(), new());
    readonly Button[] _buttons = new Button[AppSettings.CartCount];
    readonly Dictionary<string, double> _lengths = new();
    CartwallPage? _page;
    int _cart = -1;
    PreviewDeck? _deck;
    PreviewPlayer? _player;

    // Parameterless constructor for the XAML previewer.
    public CartwallsWindow() : this([], new(new(), new(), new()), _ => null) { }

    /// <param name="save">Saves the cartwalls as the station's; returns why not, or null.</param>
    public CartwallsWindow(IEnumerable<CartwallPage> pages, AudioRouting routing, Func<List<CartwallPage>, string?> save)
    {
        InitializeComponent();
        Localization.Apply(this);
        // A copy: nothing changes until Save.
        _pages = pages.Select(p => new CartwallPage
        {
            Name = p.Name,
            Carts = Enumerable.Range(0, AppSettings.CartCount)
                .Select(i => p.Carts.ElementAtOrDefault(i) is { } c ? new CartItem { Path = c.Path, Color = c.Color, Icon = c.Icon } : new CartItem()).ToList(),
        }).ToList();
        if (_pages.Count == 0) _pages.Add(NewPage(L.T("Default")));
        _routing = routing;
        _save = save;

        BuildCarts();
        BuildLooks();
        PageList.SelectionChanged += (_, _) => ShowPage();
        NewButton.Click += async (_, _) => await AddPage(copy: false);
        CopyButton.Click += async (_, _) => await AddPage(copy: true);
        RenameButton.Click += async (_, _) => await RenamePage();
        DeleteButton.Click += async (_, _) => await DeletePage();
        ChooseFileButton.Click += async (_, _) => await ChooseFile();
        ClearButton.Click += (_, _) => ChangeCart(c => c.Path = null);
        ListenButton.Click += (_, _) => Listen();
        CancelButton.Click += (_, _) => Finish(false);
        SaveButton.Click += (_, _) =>
        {
            if (_save(_pages) is { } problem) ErrorText.Text = L.F("Not saved: {0}", problem);
            else Finish(true);
        };
        Closed += (_, _) =>
        {
            _player?.Dispose();
            _deck?.Dispose();
        };
        ShowPages(0);
    }

    static CartwallPage NewPage(string name) => new() { Name = name, Carts = Enumerable.Range(0, AppSettings.CartCount).Select(_ => new CartItem()).ToList() };

    // ───────────────────────── Cartwalls ─────────────────────────

    void ShowPages(int select)
    {
        PageList.ItemsSource = _pages.Select(p => p.Name).ToList();
        PageList.SelectedIndex = Math.Clamp(select, 0, _pages.Count - 1);
        DeleteButton.IsEnabled = _pages.Count > 1;
    }

    void ShowPage()
    {
        _page = PageList.SelectedIndex is >= 0 and var i && i < _pages.Count ? _pages[i] : null;
        SelectCart(0);
        ShowCarts();
    }

    string? CheckName(string name, CartwallPage? current) =>
        name.Length == 0 ? L.T("Enter a name.")
        : _pages.Any(p => p != current && p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)) ? L.F("There is already a cartwall called “{0}”.", name)
        : null;

    async Task AddPage(bool copy)
    {
        int n = _pages.Count + 1;
        string suggested = copy && _page != null ? L.F("{0} (copy)", _page.Name) : L.F("Cartwall {0}", n);
        var name = await Dialogs.AskText(DialogOwner, copy ? L.T("Copy cartwall") : L.T("New cartwall"), L.T("Name"), suggested, t => CheckName(t, null));
        if (name == null) return;
        var page = NewPage(name);
        if (copy && _page != null)
            page.Carts = _page.Carts.Select(c => new CartItem { Path = c.Path, Color = c.Color, Icon = c.Icon }).ToList();
        _pages.Add(page);
        ShowPages(_pages.Count - 1);
    }

    async Task RenamePage()
    {
        if (_page is not { } page) return;
        var name = await Dialogs.AskText(DialogOwner, L.T("Rename cartwall"), L.T("Name"), page.Name, t => CheckName(t, page));
        if (name == null) return;
        page.Name = name;
        ShowPages(_pages.IndexOf(page));
    }

    async Task DeletePage()
    {
        if (_page is not { } page || _pages.Count < 2) return;
        if (!await Dialogs.Confirm(DialogOwner, L.T("Delete cartwall"), L.F("Delete \"{0}\"?", page.Name), L.T("Delete"))) return;
        int i = _pages.IndexOf(page);
        _pages.Remove(page);
        ShowPages(Math.Min(i, _pages.Count - 1));
    }

    // ───────────────────────── Carts ─────────────────────────

    void BuildCarts()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            int index = i;
            var button = new Button();
            button.Classes.Add("cart");
            button.Click += (_, _) => SelectCart(index);
            DragDrop.SetAllowDrop(button, true);
            button.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = Dropped(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            button.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                if (Dropped(e) is { } path)
                {
                    SelectCart(index);
                    ChangeCart(c => c.Path = path);
                }
                e.Handled = true;
            });
            _buttons[i] = button;
            CartGrid.Children.Add(button);
        }
    }

    static string? Dropped(DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetValue(AudioFiles.DragFormat)
            ?? e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        return paths?.Where(p => SharedPaths.FileExists(p) && AudioFiles.IsAudio(p)).Select(SharedPaths.ToLibrary).FirstOrDefault();
    }

    /// <summary>Draws the carts of the cartwall shown, like the playout does.</summary>
    void ShowCarts()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            var item = _page?.Carts.ElementAtOrDefault(i) ?? new CartItem();
            var name = new TextBlock
            {
                Text = item.Path == null ? "" : Path.GetFileNameWithoutExtension(item.Path), TextWrapping = TextWrapping.Wrap, FontSize = 13,
                FontWeight = FontWeight.SemiBold, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var icon = new PathIcon { Width = 14, Height = 14, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top, Data = CartLook.Icon(item.Icon) };
            icon.IsVisible = icon.Data != null;
            DockPanel.SetDock(icon, Dock.Left);
            var time = new TextBlock { FontSize = 12, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Right, Text = Length(item.Path) };
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Opacity = 0.5 };
            var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 6) };
            var title = new DockPanel { Children = { icon, name } };
            Grid.SetColumnSpan(title, 2);
            Grid.SetRow(number, 1);
            Grid.SetRow(time, 1);
            Grid.SetColumn(time, 1);
            content.Children.Add(title);
            content.Children.Add(number);
            content.Children.Add(time);
            var color = new Border { CornerRadius = new CornerRadius(5), Background = CartLook.Brush(item.Color), IsHitTestVisible = false };
            _buttons[i].Content = new Panel { Children = { color, content } };
            _buttons[i].Classes.Set("empty", item.Path == null);
            _buttons[i].Classes.Set("colored", item.Color != null);
            _buttons[i].Classes.Set("selected", i == _cart);
            ToolTip.SetTip(_buttons[i], item.Path);
        }
        ShowCart();
    }

    /// <summary>A cart file's length ("0:30"), measured once in the background; "" until known.</summary>
    string Length(string? path)
    {
        if (path == null) return "";
        if (_lengths.TryGetValue(path, out double s)) return s > 0 ? Format.Duration(Math.Ceiling(s)) : "";
        _lengths[path] = 0;
        Task.Run(() => AudioProbe.Duration(path) ?? 0).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _lengths[path] = t.IsCompletedSuccessfully ? t.Result : 0;
            ShowCarts();
        }));
        return "";
    }

    void SelectCart(int index)
    {
        _cart = index;
        for (int i = 0; i < _buttons.Length; i++) _buttons[i].Classes.Set("selected", i == _cart);
        ShowCart();
    }

    CartItem? SelectedCart => _page?.Carts.ElementAtOrDefault(_cart);

    void ShowCart()
    {
        var item = SelectedCart;
        CartPanel.IsEnabled = item != null;
        CartTitle.Text = item == null ? "" : L.F("Cart {0}", _cart + 1);
        CartFileText.Text = item?.Path ?? L.T("No sound: drop a file on the cart, or choose one.");
        ListenButton.IsEnabled = ClearButton.IsEnabled = item?.Path != null;
        foreach (var b in ColorPanel.Children.OfType<Button>()) b.Classes.Set("selected", Equals(b.Tag, item?.Color));
        foreach (var b in IconPanel.Children.OfType<Button>()) b.Classes.Set("selected", Equals(b.Tag, item?.Icon));
    }

    void ChangeCart(Action<CartItem> change)
    {
        if (SelectedCart is not { } item) return;
        change(item);
        ErrorText.Text = "";
        ShowCarts();
    }

    /// <summary>The colour and icon choices (none first).</summary>
    void BuildLooks()
    {
        foreach (var (name, hex) in new (string Name, string? Hex)[] { (L.T("None"), null) }.Concat(CartLook.Colors.Select(c => (L.T(c.Name), (string?)c.Hex))))
        {
            var b = new Button
            {
                Tag = hex, Width = 34, Height = 30, Padding = new Thickness(3),
                Content = hex == null ? new TextBlock { Text = "∅", HorizontalAlignment = HorizontalAlignment.Center }
                    : new Border { Background = CartLook.Brush(hex), CornerRadius = new CornerRadius(3), Width = 26, Height = 22 },
            };
            b.Classes.Add("cart");
            ToolTip.SetTip(b, name);
            b.Click += (_, _) => ChangeCart(c => c.Color = hex);
            ColorPanel.Children.Add(b);
        }
        foreach (var (key, name) in new (string? Key, string Name)[] { (null, L.T("None")) }.Concat(CartLook.Icons.Select(i => ((string?)i.Key, L.T(i.Name)))))
        {
            var b = new Button
            {
                Tag = key, Width = 34, Height = 30, Padding = new Thickness(0),
                Content = key == null ? new TextBlock { Text = "∅", HorizontalAlignment = HorizontalAlignment.Center }
                    : new PathIcon { Width = 14, Height = 14, Data = CartLook.Icon(key) },
            };
            b.Classes.Add("cart");
            ToolTip.SetTip(b, name);
            b.Click += (_, _) => ChangeCart(c => c.Icon = key);
            IconPanel.Children.Add(b);
        }
    }

    async Task ChooseFile()
    {
        if (SelectedCart == null) return;
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.F("Cart {0}", _cart + 1),
            FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) ChangeCart(c => c.Path = SharedPaths.ToLibrary(path));
    }

    /// <summary>The selected cart on the Preview output; again to stop.</summary>
    void Listen()
    {
        if (_player != null)
        {
            _player.Dispose();
            _player = null;
            ListenButton.Content = L.T("▶ Listen");
            return;
        }
        if (SelectedCart?.Path is not { } path) return;
        try
        {
            _deck ??= new PreviewDeck(_routing, MainWindow.ForcedDeviceFromArgs());
            _player = _deck.Open(path);
            _player?.Play();
            if (_player != null) ListenButton.Content = L.T("■ Stop");
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Can't listen: {0}", ex.Message);
        }
    }
}
