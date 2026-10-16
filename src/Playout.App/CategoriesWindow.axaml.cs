using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    readonly CategoryFolders? _folders;
    List<CategoryRow> _rows = new();
    string _color = Palette[0];
    readonly Dictionary<string, Button> _swatches = new();

    // Parameterless constructor for the XAML previewer.
    public CategoriesWindow() : this(null, null) { }

    internal CategoriesWindow(LibraryService? service, CategoryFolders? folders)
    {
        InitializeComponent();
        Localization.Apply(this);
        _service = service;
        _folders = folders;
        FoldersPanel.IsVisible = folders != null;
        AddFolderButton.Click += async (_, _) => await AddFolder();
        RemoveFolderButton.Click += async (_, _) => await RemoveFolder();

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
        // A scan started by adding a folder: the songs counts follow when it ends.
        if (service != null)
        {
            bool wasScanning = service.IsScanning;
            void Changed()
            {
                // Raised on the UI thread: the scan's state as of this change.
                bool scanning = service.IsScanning;
                if (wasScanning && !scanning) Dispatcher.UIThread.Post(async () => await Load(Selected?.Category.Id));
                wasScanning = scanning;
            }
            service.Changed += Changed;
            Closed += (_, _) => service.Changed -= Changed;
        }
    }

    CategoryRow? Selected => CategoryList.SelectedItem as CategoryRow;

    /// <summary>Reads the categories (and how many songs each has) and selects <paramref name="selectId"/>.</summary>
    async Task Load(long? selectId)
    {
        if (_service?.Db is not { } db)
        {
            StatusText.Text = _service?.Error ?? L.T("The library isn't open.");
            EditPanel.IsEnabled = false;
            return;
        }
        try
        {
            var (categories, counts, rules) = await Task.Run(() => (db.Categories(), db.CategoryCounts(), _folders?.Rules() ?? new()));
            _rows = categories.Select(c => new CategoryRow(c, counts.GetValueOrDefault(c.Id), rules.Count(r => r.Value == c.Id))).ToList();
            long? keep = selectId ?? Selected?.Category.Id;
            CategoryList.ItemsSource = _rows;
            CategoryList.SelectedItem = _rows.FirstOrDefault(r => r.Category.Id == keep) ?? _rows.FirstOrDefault();
            StatusText.Text = L.F("{0} categories", _rows.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Library unavailable: {0}", ex.Message);
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
        ShowFolders();
    }

    // ───────────────────────── Folders ─────────────────────────

    void ShowFolders()
    {
        if (_folders == null) return;
        FolderList.ItemsSource = Selected is { } row
            ? _folders.Rules().Where(r => r.Value == row.Category.Id).Select(r => r.Key).Order(StringComparer.CurrentCultureIgnoreCase).ToList()
            : null;
    }

    /// <summary>A folder whose songs go in the selected category (now, and the new ones a scan finds there).</summary>
    async Task AddFolder()
    {
        if (_folders == null || Selected is not { } row || _service?.Db is not { } db) return;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = L.F("Folder for {0}", row.Category.Name), AllowMultiple = true });
        var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().Select(p => p.TrimEnd('/', '\\')).ToList();
        if (paths.Count > 0) await AddFolders(row, db, paths);
    }

    async Task AddFolders(CategoryRow row, LibraryDb db, List<string> paths)
    {
        if (_folders == null || _service == null) return;
        int count = 0;
        try
        {
            foreach (var path in paths)
            {
                _folders.Set(path, row.Category.Id);
                _folders.EnsureScanned(path);
                count += await Task.Run(() => db.SetFolderCategory(path, row.Category.Id));
            }
            _folders.Saved();
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            return;
        }
        await Load(row.Category.Id);
        _service.NotifyChanged();
        StatusText.Text = L.F("{0}: {1} in it now; the library is scanning the folder for the rest.", row.Category.Name, Plural(count, "{0} song", "{0} songs"));
    }

    async Task RemoveFolder()
    {
        if (_folders == null || Selected is not { } row || FolderList.SelectedItem is not string path || _service?.Db is not { } db) return;
        int count;
        try
        {
            _folders.Set(path, null);
            count = await Task.Run(() => db.SetFolderCategory(path, null));
            _folders.Saved();
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            return;
        }
        await Load(row.Category.Id);
        _service.NotifyChanged();
        StatusText.Text = L.F("{0}: {1} taken out of the category", Path.GetFileName(path), Plural(count, "{0} song", "{0} songs"));
    }

    static string Plural(int n, string one, string many) => L.F(n == 1 ? one : many, n);

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
            ErrorText.Text = IsDuplicate(ex) ? L.T("There is already a category with this name.") : L.F("Not saved: {0}", ex.Message);
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
            ErrorText.Text = L.T("Give the category a name.");
            return;
        }
        if (_rows.Any(r => r != row && r.Category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = L.T("There is already a category with this name.");
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
        if (await Change(db => db.UpdateCategory(updated), row.Category.Id)) StatusText.Text = L.F("Saved {0}", name);
    }

    async Task AddCategory()
    {
        string name = L.T("New category");
        for (int i = 2; _rows.Any(r => r.Category.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = L.F("New category {0}", i);
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
            0 => L.T("No songs are in it."),
            1 => L.T("The 1 song in it will have no category."),
            _ => L.F("The {0:N0} songs in it will have no category.", row.Count),
        };
        if (!await Dialogs.Confirm(this, L.T("Delete category"), L.F("Delete \"{0}\"? {1}", row.Category.Name, songs), L.T("Delete"))) return;
        int index = _rows.IndexOf(row);
        long? next = _rows.ElementAtOrDefault(index + 1)?.Category.Id ?? _rows.ElementAtOrDefault(index - 1)?.Category.Id;
        if (await Change(db => db.DeleteCategory(row.Category.Id), next)) StatusText.Text = L.F("Deleted {0}", row.Category.Name);
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

/// <summary>
/// The folders whose songs go in a category (folder → category id, in the library database), and how the editor makes
/// sure the library scans a folder it adds, saves, and starts a scan.
/// </summary>
sealed class CategoryFolders(Func<Dictionary<string, long>> rules, Action<string, long?> set, Action<string> ensureScanned, Action saved)
{
    public Func<Dictionary<string, long>> Rules { get; } = rules;
    /// <summary>Gives a folder to a category, or (null) takes it away.</summary>
    public Action<string, long?> Set { get; } = set;
    public Action<string> EnsureScanned { get; } = ensureScanned;
    public Action Saved { get; } = saved;
}

/// <summary>A category in the editor's list.</summary>
public sealed class CategoryRow(LibraryCategory category, int count, int folders = 0)
{
    public LibraryCategory Category { get; } = category;
    public int Count { get; } = count;
    public string Name => Category.Name;
    public string KindText => (Category.Kind switch
    {
        "jingle" => L.T("Jingle"),
        "commercial" => L.T("Commercial"),
        "other" => L.T("Other"),
        _ => L.T("Music"),
    }) + (folders == 0 ? "" : " · " + (folders == 1 ? L.T("1 folder") : L.F("{0} folders", folders)));
    public string CountText => Count == 1 ? L.T("1 song") : L.F("{0:N0} songs", Count);
    public IBrush Brush { get; } = Color.TryParse(category.Color, out var c) ? new SolidColorBrush(c) : new SolidColorBrush(Color.Parse("#4A5160"));
}
