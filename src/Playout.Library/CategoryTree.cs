namespace Playout.Library;

/// <summary>What happens to a song that already has another category when a folder is put in a category.</summary>
public enum FolderAssign
{
    /// <summary>It keeps its own and is in this one too.</summary>
    AlsoIn,
    /// <summary>This becomes its category.</summary>
    Replace,
    /// <summary>It is left as it is (only songs without a category get this one).</summary>
    OnlyWithout,
}

/// <summary>Categories as a tree: Music › 80s is "80s" under "Music".</summary>
public static class CategoryTree
{
    public const string Separator = " › ";

    /// <summary>These categories and every one under them.</summary>
    public static long[] WithDescendants(IReadOnlyList<LibraryCategory> all, IEnumerable<long> ids)
    {
        var children = all.Where(c => c.ParentId != null).ToLookup(c => c.ParentId!.Value, c => c.Id);
        var found = new HashSet<long>();
        var todo = new Stack<long>(ids);
        while (todo.Count > 0)
        {
            long id = todo.Pop();
            if (!found.Add(id)) continue;
            foreach (var child in children[id]) todo.Push(child);
        }
        return found.ToArray();
    }

    /// <summary>The full name, from the top: "Music › 80s".</summary>
    public static string PathOf(IReadOnlyList<LibraryCategory> all, long id)
    {
        var byId = all.ToDictionary(c => c.Id);
        var names = new List<string>();
        var seen = new HashSet<long>();
        for (long? at = id; at is { } i && byId.TryGetValue(i, out var c) && seen.Add(i); at = c.ParentId) names.Add(c.Name);
        names.Reverse();
        return string.Join(Separator, names);
    }

    /// <summary>
    /// Every category in tree order (each followed by the ones under it, siblings in their order) with its depth (0 at
    /// the top). A category whose parent is gone counts as a top one.
    /// </summary>
    public static List<(LibraryCategory Category, int Depth)> Flatten(IReadOnlyList<LibraryCategory> all)
    {
        var ids = all.Select(c => c.Id).ToHashSet();
        var children = all.ToLookup(c => c.ParentId is { } p && ids.Contains(p) && p != c.Id ? p : (long?)null);
        var list = new List<(LibraryCategory, int)>();
        var seen = new HashSet<long>();
        void Add(long? parent, int depth)
        {
            foreach (var c in children[parent].OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!seen.Add(c.Id)) continue;
                list.Add((c, depth));
                Add(c.Id, depth + 1);
            }
        }
        Add(null, 0);
        return list;
    }
}
