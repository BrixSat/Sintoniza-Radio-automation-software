using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Playout.App;

/// <summary>
/// Cartwall: <see cref="AppSettings.CartCount"/> buttons showing one of several named cartwalls.
/// Each cart has a file and optionally a colour and an icon. Changes are saved straight away.
/// </summary>
public partial class MainWindow
{
    readonly CartSlot[] _carts = new CartSlot[AppSettings.CartCount];

    /// <summary>Carts on air, by cartwall and button, so a cart keeps playing (and can still be stopped) after switching cartwalls.</summary>
    readonly Dictionary<(CartwallPage Page, int Index), int> _playingCarts = new();

    CartwallPage _page = null!;
    bool _fillingPicker;

    sealed class CartSlot
    {
        public required Button Button;
        public required TextBlock Name;
        public required TextBlock Time;
        public required TextBlock Number;
        public required PathIcon Icon;
        public required Border Color;
        /// <summary>Two star columns: played part (with the fill) and the rest.</summary>
        public required Grid Progress;
    }

    void BuildCartwall()
    {
        _page = _settings.Cartwalls.First(c => c.Name == _settings.ActiveCartwall);

        for (int i = 0; i < _carts.Length; i++)
        {
            int index = i;
            var name = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontWeight = FontWeight.SemiBold, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            var icon = new PathIcon { Width = 14, Height = 14, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left);
            var time = new TextBlock { FontSize = 12, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Right };
            time.Classes.Add("digits");
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Opacity = 0.5 };

            var title = new DockPanel { Children = { icon, name } };
            var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 6) };
            Grid.SetColumnSpan(title, 2);
            Grid.SetRow(number, 1);
            Grid.SetRow(time, 1);
            Grid.SetColumn(time, 1);
            content.Children.Add(title);
            content.Children.Add(number);
            content.Children.Add(time);

            // The cart's own colour, under the progress fill.
            var color = new Border { CornerRadius = new CornerRadius(5), IsHitTestVisible = false };

            // Played part of the cart, filling the button from the left.
            var fill = new Border { CornerRadius = new CornerRadius(5) };
            fill.Classes.Add("cart-progress");
            var progress = new Grid { ColumnDefinitions = new ColumnDefinitions("0*,1*"), IsHitTestVisible = false };
            progress.Children.Add(fill);

            var button = new Button { Content = new Panel { Children = { color, progress, content } } };
            button.Classes.Add("cart");
            button.Click += (_, _) => ToggleCart(index);

            // Drop a file (from the music browser or the file manager) on a cart to assign it.
            DragDrop.SetAllowDrop(button, true);
            button.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                e.DragEffects = DroppedAudioFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
                button.Classes.Set("drop", e.DragEffects != DragDropEffects.None);
                e.Handled = true;
            });
            button.AddHandler(DragDrop.DragLeaveEvent, (_, _) => button.Classes.Set("drop", false));
            button.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                button.Classes.Set("drop", false);
                if (DroppedAudioFile(e) is { } path) SetCart(index, path);
                e.Handled = true;
            });

            var menu = new ContextMenu();
            menu.Opening += (_, _) => FillCartMenu(menu, index);
            button.ContextMenu = menu;

            _carts[i] = new CartSlot { Button = button, Name = name, Time = time, Number = number, Icon = icon, Color = color, Progress = progress };
            Cartwall.Children.Add(button);
        }

        CartwallPicker.SelectionChanged += (_, _) =>
        {
            if (!_fillingPicker && CartwallPicker.SelectedItem is string name && name != _page.Name) ShowCartwall(name);
        };

        var create = new MenuItem { Header = "New cartwall…" };
        create.Click += async (_, _) => await AddCartwall(copy: false);
        var saveAs = new MenuItem { Header = "Save as new cartwall…" };
        saveAs.Click += async (_, _) => await AddCartwall(copy: true);
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += async (_, _) => await RenameCartwall();
        var delete = new MenuItem { Header = "Delete…" };
        delete.Click += async (_, _) => await DeleteCartwall();
        var flyout = new MenuFlyout { Items = { create, saveAs, rename, new Separator(), delete } };
        flyout.Opening += (_, _) => delete.IsEnabled = _settings.Cartwalls.Count > 1;
        CartwallMenuButton.Flyout = flyout;

        FillCartwallPicker();
        for (int i = 0; i < _carts.Length; i++) ShowCart(i);
    }

    // ───────────────────────── Carts ─────────────────────────

    void FillCartMenu(ContextMenu menu, int index)
    {
        var item = _page.Carts[index];
        menu.Items.Clear();

        var assign = new MenuItem { Header = "Assign file…" };
        assign.Click += async (_, _) => await AssignCart(index);
        menu.Items.Add(assign);

        var colors = new MenuItem { Header = "Colour" };
        colors.Items.Add(LookItem("None", item.Color == null, null, () => item.Color = null, index));
        foreach (var (name, hex) in CartLook.Colors)
        {
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = CartLook.Brush(hex) };
            colors.Items.Add(LookItem(name, item.Color == hex, swatch, () => item.Color = hex, index));
        }
        menu.Items.Add(colors);

        var icons = new MenuItem { Header = "Icon" };
        icons.Items.Add(LookItem("None", item.Icon == null, null, () => item.Icon = null, index));
        foreach (var (key, name, _) in CartLook.Icons)
            icons.Items.Add(LookItem(name, item.Icon == key, new PathIcon { Width = 14, Height = 14, Data = CartLook.Icon(key) }, () => item.Icon = key, index));
        menu.Items.Add(icons);

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear", IsEnabled = item.Path != null || item.Color != null || item.Icon != null };
        clear.Click += (_, _) => ClearCart(index);
        menu.Items.Add(clear);
    }

    MenuItem LookItem(string name, bool current, Control? icon, Action apply, int index)
    {
        var mi = new MenuItem { Header = current ? $"{name}  ✓" : name, Icon = icon };
        mi.Click += (_, _) =>
        {
            apply();
            ShowCart(index);
            _settings.Save();
        };
        return mi;
    }

    /// <summary>The audio file being dragged (the first one, if several), or null.</summary>
    static string? DroppedAudioFile(DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetValue(AudioFiles.DragFormat)
            ?? e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        return paths?.FirstOrDefault(p => File.Exists(p) && AudioFiles.IsAudio(p));
    }

    /// <summary>Puts a file on a cart (keeping its colour and icon); stops it first if it is playing.</summary>
    void SetCart(int index, string path)
    {
        StopCart(_page, index);
        _page.Carts[index].Path = path;
        ShowCart(index);
        _settings.Save();
    }

    void ClearCart(int index)
    {
        StopCart(_page, index);
        _page.Carts[index] = new CartItem();
        ShowCart(index);
        _settings.Save();
    }

    /// <summary>Shows each cart's number and keyboard shortcut in its corner.</summary>
    void ShowCartShortcuts()
    {
        for (int i = 0; i < _carts.Length; i++)
            _carts[i].Number.Text = CartShortcut(i) is { Length: > 0 } key ? $"{i + 1} · {key}" : (i + 1).ToString();
    }

    void ShowCart(int index)
    {
        var slot = _carts[index];
        var item = _page.Carts[index];
        slot.Name.Text = item.Path == null ? "" : Path.GetFileNameWithoutExtension(item.Path);
        slot.Icon.Data = CartLook.Icon(item.Icon);
        slot.Icon.IsVisible = slot.Icon.Data != null;
        slot.Color.Background = CartLook.Brush(item.Color);
        slot.Button.Classes.Set("empty", item.Path == null);
        slot.Button.Classes.Set("colored", item.Color != null);
        ToolTip.SetTip(slot.Button, item.Path);
    }

    async Task AssignCart(int index)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Assign cart {index + 1}",
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = AudioFiles.Patterns }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SetCart(index, path);
    }

    void ToggleCart(int index)
    {
        if (_engine == null || _page.Carts[index].Path is not { } path) return;
        if (_playingCarts.ContainsKey((_page, index))) StopCart(_page, index);
        else if (_engine.FireCart(path) is var h && h != 0) _playingCarts[(_page, index)] = h;
    }

    void StopCart(CartwallPage page, int index)
    {
        if (_playingCarts.Remove((page, index), out int handle)) _engine?.StopCart(handle);
    }

    void UpdateCarts()
    {
        // Forget carts that have finished, on any cartwall.
        var progress = new Dictionary<(CartwallPage, int), (double Left, double Played)>();
        foreach (var (key, handle) in _playingCarts.ToList())
        {
            if (_engine?.CartProgress(handle) is { } p) progress[key] = p;
            else _playingCarts.Remove(key);
        }

        for (int i = 0; i < _carts.Length; i++)
        {
            var slot = _carts[i];
            bool playing = progress.TryGetValue((_page, i), out var p);
            slot.Button.Classes.Set("playing", playing);
            slot.Time.Text = playing ? "-" + Format.Duration(Math.Ceiling(p.Left)) : "";
            double played = playing ? p.Played : 0;
            slot.Progress.ColumnDefinitions[0].Width = new GridLength(played, GridUnitType.Star);
            slot.Progress.ColumnDefinitions[1].Width = new GridLength(1 - played, GridUnitType.Star);
        }
    }

    // ───────────────────────── Cartwalls ─────────────────────────

    void ShowCartwall(string name)
    {
        _page = _settings.Cartwalls.First(c => c.Name == name);
        _settings.ActiveCartwall = name;
        for (int i = 0; i < _carts.Length; i++) ShowCart(i);
        UpdateCarts();
        FillCartwallPicker();
        _settings.Save();
    }

    void FillCartwallPicker()
    {
        _fillingPicker = true;
        CartwallPicker.ItemsSource = _settings.Cartwalls.Select(c => c.Name).ToList();
        CartwallPicker.SelectedItem = _page.Name;
        _fillingPicker = false;
    }

    /// <summary>Error for a cartwall name, or null if it can be used. <paramref name="current"/> may keep its own name.</summary>
    string? CheckCartwallName(string name, CartwallPage? current)
    {
        if (name.Length == 0) return "Enter a name.";
        if (_settings.Cartwalls.Any(c => c != current && string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return $"There is already a cartwall called “{name}”.";
        return null;
    }

    /// <summary>Creates a cartwall, empty or as a copy of the one shown, and switches to it.</summary>
    async Task AddCartwall(bool copy)
    {
        int n = _settings.Cartwalls.Count + 1;
        while (CheckCartwallName($"Cartwall {n}", null) != null) n++;
        string? name = await Dialogs.AskText(this, copy ? "Save as new cartwall" : "New cartwall", "Name of the cartwall:",
            copy ? $"{_page.Name} copy" : $"Cartwall {n}", text => CheckCartwallName(text, null));
        if (name == null) return;

        var carts = copy
            ? _page.Carts.Select(c => new CartItem { Path = c.Path, Color = c.Color, Icon = c.Icon }).ToList()
            : Enumerable.Range(0, AppSettings.CartCount).Select(_ => new CartItem()).ToList();
        _settings.Cartwalls.Add(new CartwallPage { Name = name, Carts = carts });
        ShowCartwall(name);
    }

    async Task RenameCartwall()
    {
        var page = _page;
        string? name = await Dialogs.AskText(this, "Rename cartwall", "Name of the cartwall:", page.Name, text => CheckCartwallName(text, page));
        if (name == null || name == page.Name) return;
        page.Name = name;
        if (_page == page) _settings.ActiveCartwall = name;
        FillCartwallPicker();
        _settings.Save();
    }

    async Task DeleteCartwall()
    {
        if (_settings.Cartwalls.Count <= 1) return;
        var page = _page;
        if (!await Dialogs.Confirm(this, "Delete cartwall",
                $"Delete the cartwall “{page.Name}”? Its carts are removed; the audio files are not touched.", "Delete"))
            return;
        for (int i = 0; i < AppSettings.CartCount; i++) StopCart(page, i);
        _settings.Cartwalls.Remove(page);
        if (_page == page) ShowCartwall(_settings.Cartwalls[0].Name);
        else FillCartwallPicker();
        _settings.Save();
    }
}
