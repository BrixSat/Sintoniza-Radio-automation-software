using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Adds, renames, recolours, reorders and deletes the library's categories. Changes are saved to the library
/// straight away (other studios sharing it see them too).
/// </summary>
public partial class CategoriesWindow : Window
{
    static readonly string[] Kinds = ["music", "jingle", "commercial", "other"];

    static readonly string[] Palette =
    [
        "#3E8EDE", "#2FA37A", "#D6A21E", "#B4232A", "#C2185B", "#7B5CC4",
        "#1F6F4A", "#E07B39", "#0097A7", "#6D7B8D", "#8D6E63", "#5C6BC0",
    ];

    readonly LibraryService? _service;
    List<CategoryRow> _rows = new();
    string _color = Palette[0];
    readonly Dictionary<string, Button> _swatches = new();

    // Parameterless constructor for the XAML previewer.
    public CategoriesWindow() : this(null) { }

    internal CategoriesWindow(LibraryService? service)
    {
        InitializeComponent();
        _service = service;

        foreach (var color in Palette)
        {
            var swatch = new Button { Background = new SolidColorBrush(Color.Parse(color)) };
            swatch.Classes.Add("swatch");
            swatch.Click += (_, _) => ShowColor(color);
            _swatches[color] = swatch;
            Swatches.Children.Add(swatch);
        }

        CategoryList.SelectionChanged += (_, _) => ShowSelected();
        AddButton.Click += async (_, _) => await AddCategory();
        DeleteButton.Click += async (_, _) => await DeleteCategory();
        UpButton.Click += async (_, _) => await Move(-1);
        DownButton.Click += async (_, _) => await Move(1);
        ApplyButton.Click += async (_, _) => await SaveSelected();
        CloseButton.Click += (_, _) => Close();
        Opened += async (_, _) => await Load(null);
    }

    CategoryRow? Selected => CategoryList.SelectedItem as CategoryRow;

    /// <summary>Reads the categories (and how many songs each has) and selects <paramref name="selectId"/>.</summary>
    async Task Load(long? selectId)
    {
        if (_service?.Db is not { } db)
        {
            StatusText.Text = _service?.Error ?? "The library isn't open.";
            EditPanel.IsEnabled = false;
            return;
        }
        try
        {
            var (categories, counts) = await Task.Run(() => (db.Categories(), db.CategoryCounts()));
            _rows = categories.Select(c => new CategoryRow(c, counts.GetValueOrDefault(c.Id))).ToList();
            long? keep = selectId ?? Selected?.Category.Id;
            CategoryList.ItemsSource = _rows;
            CategoryList.SelectedItem = _rows.FirstOrDefault(r => r.Category.Id == keep) ?? _rows.FirstOrDefault();
            StatusText.Text = $"{_rows.Count} categories";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Library unavailable: {ex.Message}";
        }
        ShowSelected();
    }

    void ShowSelected()
    {
        ErrorText.Text = "";
        var row = Selected;
        EditPanel.IsEnabled = row != null;
        DeleteButton.IsEnabled = row != null;
        int index = row == null ? -1 : _rows.IndexOf(row);
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < _rows.Count - 1;
        if (row == null) return;
        NameBox.Text = row.Category.Name;
        KindBox.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, row.Category.Kind));
        ShowColor(row.Category.Color ?? Palette[0]);
    }

    void ShowColor(string color)
    {
        _color = color;
        foreach (var (c, swatch) in _swatches) swatch.Classes.Set("chosen", c.Equals(color, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Runs a change on the library, then reloads the list and tells the library panel.</summary>
    async Task<bool> Change(Action<LibraryDb> change, long? selectId = null)
    {
        if (_service?.Db is not { } db) return false;
        try
        {
            await Task.Run(() => change(db));
        }
        catch (Exception ex)
        {
            ErrorText.Text = IsDuplicate(ex) ? "There is already a category with this name." : $"Not saved: {ex.Message}";
            return false;
        }
        await Load(selectId);
        _service.NotifyChanged();
        return true;
    }

    static bool IsDuplicate(Exception ex) =>
        ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase);

    async Task SaveSelected()
    {
        if (Selected is not { } row) return;
        string name = NameBox.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the category a name.";
            return;
        }
        if (_rows.Any(r => r != row && r.Category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = "There is already a category with this name.";
            return;
        }
        var updated = new LibraryCategory
        {
            Id = row.Category.Id,
            Name = name,
            Kind = Kinds[Math.Max(0, KindBox.SelectedIndex)],
            Color = _color,
            SortOrder = row.Category.SortOrder,
        };
        if (await Change(db => db.UpdateCategory(updated), row.Category.Id)) StatusText.Text = $"Saved {name}";
    }

    async Task AddCategory()
    {
        string name = "New category";
        for (int i = 2; _rows.Any(r => r.Category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"New category {i}";
        // A colour not used yet, if there is one.
        string color = Palette.FirstOrDefault(p => _rows.All(r => !p.Equals(r.Category.Color, StringComparison.OrdinalIgnoreCase))) ?? Palette[0];
        long id = 0;
        if (!await Change(db => id = db.AddCategory(name, "music", color))) return;
        await Load(id);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    async Task DeleteCategory()
    {
        if (Selected is not { } row) return;
        string songs = row.Count switch
        {
            0 => "No songs are in it.",
            1 => "The 1 song in it will have no category.",
            _ => $"The {row.Count:N0} songs in it will have no category.",
        };
        if (!await Dialogs.Confirm(this, "Delete category", $"Delete \"{row.Category.Name}\"? {songs}", "Delete")) return;
        int index = _rows.IndexOf(row);
        long? next = _rows.ElementAtOrDefault(index + 1)?.Category.Id ?? _rows.ElementAtOrDefault(index - 1)?.Category.Id;
        if (await Change(db => db.DeleteCategory(row.Category.Id), next)) StatusText.Text = $"Deleted {row.Category.Name}";
    }

    /// <summary>Moves the selected category up or down; the order is saved as 1, 2, 3… for all of them.</summary>
    async Task Move(int by)
    {
        if (Selected is not { } row) return;
        int index = _rows.IndexOf(row), to = index + by;
        if (to < 0 || to >= _rows.Count) return;
        var order = _rows.Select(r => r.Category).ToList();
        (order[index], order[to]) = (order[to], order[index]);
        var changed = new List<LibraryCategory>();
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i].SortOrder == i + 1) continue;
            order[i].SortOrder = i + 1;
            changed.Add(order[i]);
        }
        await Change(db => { foreach (var c in changed) db.UpdateCategory(c); }, row.Category.Id);
    }
}

/// <summary>A category in the editor's list.</summary>
public sealed class CategoryRow(LibraryCategory category, int count)
{
    public LibraryCategory Category { get; } = category;
    public int Count { get; } = count;
    public string Name => Category.Name;
    public string KindText => Category.Kind switch
    {
        "jingle" => "Jingle",
        "commercial" => "Commercial",
        "other" => "Other",
        _ => "Music",
    };
    public string CountText => Count == 1 ? "1 song" : $"{Count:N0} songs";
    public IBrush Brush { get; } = Color.TryParse(category.Color, out var c) ? new SolidColorBrush(c) : new SolidColorBrush(Color.Parse("#4A5160"));
}
