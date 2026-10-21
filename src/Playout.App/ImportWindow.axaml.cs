using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Import (its own menu entry): brings a folder of audio into the library, in a category, with the folders
/// inside it as categories under that one. A preview shows what will be made before anything is changed.
/// </summary>
public partial class ImportWindow : PageWindow
{
    static readonly string[] Kinds = ["music", "jingle", "commercial", "other"];

    readonly LibraryDb? _db;
    IReadOnlyList<LibraryCategory> _categories = [];
    ImportNode? _plan;
    CancellationTokenSource? _running;
    string _autoName = ""; // the name taken from the folder, replaced when another folder is chosen

    // Parameterless constructor for the XAML previewer.
    public ImportWindow() : this(null, null) { }

    internal ImportWindow(LibraryDb? db, string? error)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        LibraryText.Text = db != null ? L.F("Library: {0}", db.Location) : L.F("The library can't be opened: {0}", error);
        if (db == null)
        {
            PreviewButton.IsEnabled = false;
            ChooseButton.IsEnabled = false;
        }

        ChooseButton.Click += async (_, _) => await ChooseFolder();
        PreviewButton.Click += async (_, _) => await Preview();
        ImportButton.Click += async (_, _) => await Import();
        StopButton.Click += (_, _) => _running?.Cancel();
        CloseButton.Click += (_, _) => Finish();
        // Anything changed after a preview: it must be looked at again before importing.
        FolderBox.TextChanged += (_, _) => Stale();
        NameBox.TextChanged += (_, _) => Stale();
        ParentBox.SelectionChanged += (_, _) =>
        {
            // New categories take the kind of the one they go under (Spots: adverts, Jingles: jingles…).
            if (_categories.FirstOrDefault(c => c.Id == ParentId) is { } parent && Array.IndexOf(Kinds, parent.Kind) is >= 0 and var k) KindBox.SelectedIndex = k;
            Stale();
        };
        LevelsBox.SelectionChanged += (_, _) => Stale();
        Closing += (_, e) =>
        {
            if (_running == null) return;
            e.Cancel = true; // half an import is fine (it can run again), but say so first
            StatusText.Text = L.T("Importing: click Stop first.");
        };
        Opened += async (_, _) => await LoadCategories(null);
    }

    async Task LoadCategories(long? select)
    {
        if (_db is not { } db) return;
        try
        {
            _categories = await Task.Run(db.Categories);
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Library unavailable: {0}", ex.Message);
            return;
        }
        // Under one of the station's eight categories (Music first), or a category under them.
        var choices = CategoryTree.Flatten(_categories).Select(x => new ParentChoice(x.Category.Id, CategoryTree.PathOf(_categories, x.Category.Id))).ToList();
        if (choices.Count == 0) return;
        long? keep = select ?? (ParentBox.SelectedItem as ParentChoice)?.Id ?? _categories.FirstOrDefault(c => c.Role == "music")?.Id;
        ParentBox.ItemsSource = choices;
        ParentBox.SelectedItem = choices.FirstOrDefault(c => c.Id == keep) ?? choices[0];
    }

    sealed record ParentChoice(long? Id, string Name)
    {
        public override string ToString() => Name;
    }

    async Task ChooseFolder()
    {
        var picked = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Folder to import") });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        FolderBox.Text = path.TrimEnd('/', '\\');
        // The category is named after the folder, unless a name was typed.
        string name = Path.GetFileName(FolderBox.Text);
        if (string.IsNullOrWhiteSpace(NameBox.Text) || NameBox.Text == _autoName) NameBox.Text = _autoName = name;
    }

    void Stale()
    {
        if (_plan == null) return;
        _plan = null;
        ImportButton.IsEnabled = false;
        StatusText.Text = L.T("Changed: click Preview import again before importing.");
    }

    int Levels => Math.Max(0, LevelsBox.SelectedIndex);
    long? ParentId => (ParentBox.SelectedItem as ParentChoice)?.Id;

    FolderAssign How => ReplaceRadio.IsChecked == true ? FolderAssign.Replace
        : LeaveRadio.IsChecked == true ? FolderAssign.OnlyWithout
        : FolderAssign.AlsoIn;

    async Task Preview()
    {
        if (_db is not { } db || _running != null) return;
        string folder = FolderBox.Text?.Trim() ?? "";
        string name = NameBox.Text?.Trim() ?? "";
        if (!Directory.Exists(folder)) { StatusText.Text = L.T("Choose a folder that exists."); return; }
        if (name.Length == 0) { StatusText.Text = L.T("Give the category a name."); return; }

        long? parent = ParentId;
        int levels = Levels;
        StatusText.Text = L.T("Looking through the folder…");
        PreviewButton.IsEnabled = false;
        try
        {
            var plan = await Task.Run(() => FolderImport.Plan(db, folder, name, parent, levels));
            ShowPlan(plan);
            _plan = plan.Files + plan.Children.Count > 0 ? plan : null;
            ImportButton.IsEnabled = _plan != null;
            StatusText.Text = _plan == null ? L.T("There are no audio files in this folder.") : L.T("This is what Import will do.");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Can't look through the folder: {0}", ex.Message);
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }
    }

    void ShowPlan(ImportNode plan)
    {
        var nodes = plan.All().ToList();
        PlanList.ItemsSource = nodes.Select(n => new ImportRow(n)).ToList();
        EmptyText.IsVisible = false;
        int made = nodes.Count(n => n.ExistingId == null);
        TotalText.Text = L.F("{0} categories ({1} new) · {2:N0} audio files · {3:N0} already in the library · {4:N0} already in another category",
            nodes.Count, made, nodes.Sum(n => n.Files), nodes.Sum(n => n.InLibrary), nodes.Sum(n => n.InOtherCategory));
    }

    /// <summary>An import finished (or was stopped): categories and songs may be new.</summary>
    public event Action? Imported;

    /// <summary>The categories again (made elsewhere meanwhile), keeping the one chosen.</summary>
    public Task Refresh() => _running != null ? Task.CompletedTask : LoadCategories(ParentId);

    async Task Import()
    {
        if (_db is not { } db || _plan is not { } plan || _running != null) return;
        long? parent = ParentId;
        string kind = Kinds[Math.Clamp(KindBox.SelectedIndex, 0, Kinds.Length - 1)];
        // New categories: the colour of the one they go under, or one not used yet.
        string color = _categories.FirstOrDefault(c => c.Id == parent)?.Color
            ?? CategoriesWindow.Palette.FirstOrDefault(p => _categories.All(c => !p.Equals(c.Color, StringComparison.OrdinalIgnoreCase)))
            ?? CategoriesWindow.Palette[0];
        var how = How;

        _running = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<ScanProgress>(p =>
        {
            Progress.IsIndeterminate = p.Read == 0;
            Progress.Maximum = Math.Max(1, p.ToRead);
            Progress.Value = p.Read;
            StatusText.Text = p.Read == 0
                ? L.F("Looking for files: {0:N0} found, {1:N0} new or changed", p.Found, p.ToRead)
                : L.F("Reading tags: {0:N0} of {1:N0}", p.Read, p.ToRead);
        });
        var step = new Progress<string>(s => StatusText.Text = s switch
        {
            "categories" => L.T("Making the categories…"),
            "assign" => L.T("Putting the songs in their categories…"),
            _ => StatusText.Text,
        });
        try
        {
            var token = _running.Token;
            var r = await Task.Run(() => FolderImport.Run(db, plan, parent, kind, color, how, progress, step, token));
            string summary = r.Cancelled
                ? L.F("Stopped. {0} categories made; the songs read so far are in the library. Import again to finish.", r.CategoriesMade)
                : L.F("Done: {0} categories made, {1:N0} songs added, {2:N0} updated, {3:N0} put in a category.",
                    r.CategoriesMade, r.Scan.Added, r.Scan.Updated, r.Assigned);
            StatusText.Text = summary;
            AppLog.Write($"Import of {plan.Folder} into {plan.FullName}: {summary}");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("The import stopped: {0}", ex.Message);
            AppLog.Write($"Import of {plan.Folder} failed: {ex.Message}");
        }
        finally
        {
            _running.Dispose();
            _running = null;
            SetBusy(false);
        }
        Imported?.Invoke(); // the rest of the program shows the new categories and songs
        // What is in the library now (the new categories are there, so the preview shows them as such).
        _plan = null;
        ImportButton.IsEnabled = false;
        await LoadCategories(parent);
        try { ShowPlan(await Task.Run(() => FolderImport.Plan(db, plan.Folder, plan.Name, parent, Levels))); }
        catch { /* the summary above says what happened */ }
    }

    void SetBusy(bool busy)
    {
        Progress.IsVisible = busy;
        Progress.IsIndeterminate = true;
        StopButton.IsVisible = busy;
        ImportButton.IsEnabled = false;
        foreach (var c in new Control[] { FolderBox, ChooseButton, NameBox, ParentBox, KindBox, LevelsBox, AlsoInRadio, ReplaceRadio, LeaveRadio, PreviewButton, CloseButton })
            c.IsEnabled = !busy;
    }
}

/// <summary>A category in the import's preview.</summary>
public sealed class ImportRow(ImportNode node)
{
    public string Name => node.Name;
    public string Folder => node.Folder;
    public string FolderName => node.Depth == 0 ? node.Folder : Path.GetFileName(node.Folder) == node.Name ? "" : Path.GetFileName(node.Folder);
    public Thickness Indent { get; } = new(22 * node.Depth, 0, 0, 0);
    public string Tag => node.ExistingId == null ? L.T("NEW") : L.T("ALREADY THERE");
    public IBrush TagBrush { get; } = new SolidColorBrush(Color.Parse(node.ExistingId == null ? "#2FA37A" : "#4A5160"));
    public string Files => node.Files.ToString("N0");
    public string InLibrary => node.InLibrary.ToString("N0");
    public string InOther => node.InOtherCategory.ToString("N0");
}
