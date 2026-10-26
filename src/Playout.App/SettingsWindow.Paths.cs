using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>Settings → Library → This computer's paths: where this computer sees the library's music.</summary>
public partial class SettingsWindow
{
    readonly List<(TextBox Library, TextBox Local)> _pathRows = new();

    void LoadPathMap(IEnumerable<PathMapping> map)
    {
        foreach (var m in map) AddPathRow(m.Library, m.Local);
        AddPathMapButton.Click += (_, _) => AddPathRow(SuggestLibraryStart(), "");
        TestPathMapButton.Click += async (_, _) => await TestPathMap();
    }

    List<PathMapping> PathMapFromUi() => _pathRows
        .Select(r => new PathMapping { Library = r.Library.Text?.Trim() ?? "", Local = r.Local.Text?.Trim() ?? "" })
        .Where(m => m.Library.Length > 0 && m.Local.Length > 0)
        .ToList();

    void AddPathRow(string library, string local)
    {
        var libraryBox = new TextBox { Text = library, PlaceholderText = "/mnt/radio" };
        var localBox = new TextBox { Text = local, PlaceholderText = OperatingSystem.IsWindows() ? @"Z:\" : OperatingSystem.IsMacOS() ? "/Volumes/radio" : "/mnt/radio" };
        var folder = new Button { Content = L.T("Folder…") };
        var remove = new Button { Content = L.T("Remove") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { folder, remove } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,Auto") };
        Grid.SetColumn(localBox, 2);
        Grid.SetColumn(buttons, 4);
        grid.Children.Add(libraryBox);
        grid.Children.Add(localBox);
        grid.Children.Add(buttons);
        var row = (libraryBox, localBox);
        _pathRows.Add(row);
        PathMapRows.Children.Add(grid);
        folder.Click += async (_, _) =>
        {
            var picked = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Where is it on this computer?") });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) localBox.Text = path;
        };
        remove.Click += (_, _) =>
        {
            _pathRows.Remove(row);
            PathMapRows.Children.Remove(grid);
        };
    }

    /// <summary>
    /// For a new row: the start the library's songs have in common (what is shared), when it isn't a path of this
    /// computer already.
    /// </summary>
    string SuggestLibraryStart()
    {
        try
        {
            if (_main?.MusicLibrary.Db is not { } db) return "";
            var paths = db.Search("", new CategoryFilter(), 200).Tracks.Select(t => t.Path).ToList();
            if (paths.Count == 0) return "";
            string common = paths.Aggregate(CommonStart);
            int cut = Math.Max(common.LastIndexOf('/'), common.LastIndexOf('\\'));
            common = cut > 0 ? common[..cut] : "";
            return common.Length > 0 && !Directory.Exists(common) ? common : "";
        }
        catch
        {
            return "";
        }

        static string CommonStart(string a, string b)
        {
            int n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return a[..n];
        }
    }

    /// <summary>A few of the library's songs looked for on this computer, with the paths as they are in the window.</summary>
    async Task TestPathMap()
    {
        if (_main?.MusicLibrary.Db is not { } db)
        {
            PathMapNote.Text = L.T("The library isn't open: save, then test.");
            return;
        }
        var map = PathMapFromUi().Select(m => new SharedPaths.Mapping(m.Library, m.Local)).ToList();
        PathMapNote.Text = L.T("Looking…");
        try
        {
            var (found, total, example) = await Task.Run<(int, int, (string Library, string Local)?)>(() =>
            {
                var sample = db.Search("", new CategoryFilter(), 2000).Tracks.Select(t => t.Path)
                    .Where(p => !Takes.IsTake(p)).OrderBy(_ => Random.Shared.Next()).Take(20).ToList();
                // With the paths in the window; the program's own stay as they are until Save.
                bool There(string p) => File.Exists(SharedPaths.ToLocal(p, map));
                var missing = sample.FirstOrDefault(p => !There(p));
                return (sample.Count(There), sample.Count, missing == null ? null : (missing, SharedPaths.ToLocal(missing, map)));
            });
            PathMapNote.Text = total == 0 ? L.T("The library has no songs yet.")
                : found == total ? L.F("✓ {0} of {1} songs found on this computer.", found, total)
                : L.F("{0} of {1} songs found on this computer. Not found, e.g.: {2} (looked for as {3}).", found, total, example?.Library ?? "", example?.Local ?? "");
        }
        catch (Exception ex)
        {
            PathMapNote.Text = ex.Message;
        }
    }
}
