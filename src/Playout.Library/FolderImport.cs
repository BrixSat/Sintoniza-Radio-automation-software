using Dapper;

namespace Playout.Library;

/// <summary>
/// One category an import makes (or finds already there) and the folder its songs come from. The songs in the folders
/// of the categories under it go in those instead.
/// </summary>
public sealed class ImportNode
{
    public string Name { get; init; } = "";
    /// <summary>The full name, from the top: Music › 80s.</summary>
    public string FullName { get; init; } = "";
    public string Folder { get; init; } = "";
    /// <summary>0 for the category chosen, 1 for those under it, and so on.</summary>
    public int Depth { get; init; }
    /// <summary>The category already in the library with this name and place; null: it will be made.</summary>
    public long? ExistingId { get; set; }
    public List<ImportNode> Children { get; } = new();
    /// <summary>Audio files that go in this category (not those of the categories under it).</summary>
    public int Files { get; set; }
    /// <summary>Of those, already in the library.</summary>
    public int InLibrary { get; set; }
    /// <summary>Of those, already in another category.</summary>
    public int InOtherCategory { get; set; }

    /// <summary>This and every category under it, each before its own.</summary>
    public IEnumerable<ImportNode> All() => Children.SelectMany(c => c.All()).Prepend(this);
}

public sealed record ImportResult(int CategoriesMade, ScanResult Scan, int Assigned, bool Cancelled);

/// <summary>
/// Imports a folder of audio into a category: the folders inside it (down to a chosen depth) become categories under
/// it (Music › 80s, Music › 80s › Rock…), the songs are read into the library and put in them, and each folder is kept
/// for its category, so new songs put there later get it too. <see cref="Plan"/> only looks (for the preview);
/// <see cref="Run"/> does it.
/// </summary>
public static class FolderImport
{
    static readonly EnumerationOptions Here = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
    static readonly EnumerationOptions Deep = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };

    /// <summary>
    /// What an import would do, changing nothing: the categories (with those already there) and how many files go in
    /// each. Folders without audio files give no category.
    /// </summary>
    /// <param name="name">The category for the folder itself.</param>
    /// <param name="parentId">Where that category goes (null: the top).</param>
    /// <param name="levels">How many levels of folders inside become categories (0: all in the one category).</param>
    public static ImportNode Plan(LibraryDb db, string folder, string name, long? parentId, int levels, CancellationToken cancel = default)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var categories = db.Categories();
        string top = parentId is { } p ? CategoryTree.PathOf(categories, p) : "";
        var root = Node(folder, name.Trim(), top.Length > 0 ? top + CategoryTree.Separator + name.Trim() : name.Trim(), 0, levels, cancel)
                   ?? new ImportNode { Name = name.Trim(), FullName = name.Trim(), Folder = folder };
        Match(root, parentId, categories);

        // What the library already has under the folder: each song counts for the deepest category whose folder it's in.
        var nodes = root.All().OrderByDescending(n => n.Folder.Length).ToList();
        using var c = db.Connect();
        var args = new DynamicParameters();
        string under = LibraryDb.Under(folder, "f", args);
        foreach (var (path, category) in c.Query<(string, long?)>($"SELECT path, category_id FROM tracks WHERE missing = 0 AND {under}", args))
        {
            cancel.ThrowIfCancellationRequested();
            if (nodes.FirstOrDefault(n => LibraryScanner.IsUnder(path, n.Folder)) is not { } node) continue;
            node.InLibrary++;
            if (category is { } id && id != node.ExistingId) node.InOtherCategory++;
        }
        return root;
    }

    static ImportNode? Node(string folder, string name, string fullName, int depth, int levels, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var node = new ImportNode { Name = name, FullName = fullName, Folder = folder, Depth = depth };
        if (depth >= levels)
        {
            node.Files = Count(folder, Deep);
        }
        else
        {
            node.Files = Count(folder, Here);
            foreach (var sub in Directories(folder).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                string subName = Path.GetFileName(sub);
                if (Node(sub, subName, fullName + CategoryTree.Separator + subName, depth + 1, levels, cancel) is { } child)
                    node.Children.Add(child);
            }
        }
        return node.Files > 0 || node.Children.Count > 0 ? node : null;
    }

    static IEnumerable<string> Directories(string folder)
    {
        try { return Directory.EnumerateDirectories(folder, "*", Here).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    static int Count(string folder, EnumerationOptions options)
    {
        try { return Directory.EnumerateFiles(folder, "*", options).Count(LibraryScanner.IsAudio); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>The categories already there: same name (any case) under the same parent. Under a new one, all are new.</summary>
    static void Match(ImportNode node, long? parentId, IReadOnlyList<LibraryCategory> categories, bool parentThere = true)
    {
        node.ExistingId = parentThere
            ? categories.FirstOrDefault(c => c.ParentId == parentId && c.Name.Equals(node.Name, StringComparison.CurrentCultureIgnoreCase))?.Id
            : null;
        foreach (var child in node.Children) Match(child, node.ExistingId, categories, node.ExistingId != null);
    }

    /// <summary>
    /// Does the import of a <see cref="Plan"/>: makes the categories that aren't there (new ones under
    /// <paramref name="parentId"/>, of <paramref name="kind"/>, with <paramref name="color"/>), reads the folder into the
    /// library, puts its songs in their categories (<paramref name="how"/>: what happens to songs that already have
    /// another) and keeps each folder for its category.
    /// </summary>
    public static ImportResult Run(LibraryDb db, ImportNode plan, long? parentId, string kind, string? color, FolderAssign how,
        IProgress<ScanProgress>? progress = null, IProgress<string>? step = null, CancellationToken cancel = default)
    {
        int made = 0;
        void Make(ImportNode node, long? parent)
        {
            if (node.ExistingId == null)
            {
                node.ExistingId = db.AddCategory(node.Name, kind, color, parent);
                made++;
            }
            foreach (var child in node.Children) Make(child, node.ExistingId);
        }
        step?.Report("categories");
        Make(plan, parentId);

        step?.Report("scan");
        var scan = LibraryScanner.Scan(db, [plan.Folder], progress, cancel);
        if (scan.Cancelled) return new ImportResult(made, scan, 0, true);

        step?.Report("assign");
        int assigned = 0;
        foreach (var node in plan.All())
        {
            long id = node.ExistingId!.Value;
            assigned += db.AssignFolder(node.Folder, node.Children.Select(c => c.Folder).ToList(), id, how);
            db.SetFolderRule(node.Folder, id);
        }
        return new ImportResult(made, scan, assigned, false);
    }
}
