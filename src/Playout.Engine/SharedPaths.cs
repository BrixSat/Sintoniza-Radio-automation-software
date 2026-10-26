namespace Playout.Engine;

/// <summary>
/// Music on a share seen under a different path on each computer — /mnt/radio on the Linux on-air PC, Z:\ or
/// \\server\radio on Windows, /Volumes/radio on a Mac. The library, the schedule and the playlists keep one form (the
/// library's); each computer says where that is for it (Settings → Library → This computer's paths). A path is turned
/// into this computer's form where a file is opened, and into the library's where one comes in (a scan, a file picked
/// or dropped). With no paths set, nothing changes.
/// </summary>
public static class SharedPaths
{
    /// <summary>A start of the library's paths and where that is on this computer.</summary>
    public sealed record Mapping(string Library, string Local);

    static volatile Mapping[] _map = [];

    /// <summary>This computer's paths (empty: the library's paths are this computer's).</summary>
    public static IReadOnlyList<Mapping> Map => _map;

    public static void Set(IEnumerable<Mapping>? map) => _map = Clean(map ?? []);

    static Mapping[] Clean(IEnumerable<Mapping> map) => map.Where(m => !string.IsNullOrWhiteSpace(m.Library) && !string.IsNullOrWhiteSpace(m.Local))
            .Select(m => new Mapping(Trim(m.Library), Trim(m.Local)))
            .Where(m => m.Library.Length > 0 && m.Local.Length > 0)
            .ToArray();

    /// <summary>The path as this computer opens it (unchanged when it isn't under any of the library's starts).</summary>
    public static string ToLocal(string path) => Local(path, _map);

    /// <summary>With these paths rather than this program's (to try them before they are saved).</summary>
    public static string ToLocal(string path, IEnumerable<Mapping> map) => Local(path, Clean(map));

    static string Local(string path, Mapping[] map)
    {
        if (string.IsNullOrEmpty(path)) return path;
        foreach (var m in map.OrderByDescending(m => m.Library.Length))
            if (Rest(path, m.Library) is { } rest)
                return Join(m.Local, rest, LocalSeparator(m.Local));
        return path;
    }

    /// <summary>The path as the library keeps it (unchanged when it isn't under any of this computer's starts).</summary>
    public static string ToLibrary(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        foreach (var m in _map.OrderByDescending(m => m.Local.Length))
            if (Rest(path, m.Local) is { } rest)
                return Join(m.Library, rest, Separator(m.Library));
        return path;
    }

    /// <summary>The file is there, on this computer.</summary>
    public static bool FileExists(string path) => File.Exists(ToLocal(path));

    /// <summary>The folder is there, on this computer.</summary>
    public static bool DirectoryExists(string path) => Directory.Exists(ToLocal(path));

    /// <summary>A path in the library's form that this computer opens elsewhere.</summary>
    public static bool IsMapped(string path) => !string.IsNullOrEmpty(path) && _map.Any(m => Rest(path, m.Library) != null);

    /// <summary>Whatever follows <paramref name="start"/> in <paramref name="path"/> ("" for the start itself), or null.</summary>
    static string? Rest(string path, string start)
    {
        if (path.Length < start.Length) return null;
        var comparison = IsWindows(start) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (int i = 0; i < start.Length; i++)
        {
            char a = path[i], b = start[i];
            if (IsSeparator(a) && IsSeparator(b)) continue;
            if (!string.Equals(a.ToString(), b.ToString(), comparison)) return null;
        }
        if (path.Length == start.Length) return "";
        if (!IsSeparator(path[start.Length])) return null; // /mnt/radio2 isn't under /mnt/radio
        return path[(start.Length + 1)..];
    }

    static string Join(string start, string rest, char separator)
    {
        if (rest.Length == 0) return start.Length == 2 && start[1] == ':' ? start + "\\" : start; // Z: is "Z:\"
        rest = rest.Replace('/', separator).Replace('\\', separator);
        return start + separator + rest;
    }

    static string Trim(string path)
    {
        path = path.Trim();
        // "Z:\" and "/" keep what makes them a root; the rest lose a separator at the end.
        while (path.Length > 1 && IsSeparator(path[^1]) && !(path.Length == 3 && path[1] == ':')) path = path[..^1];
        if (path.Length == 3 && path[1] == ':') path = path[..2];
        return path;
    }

    static bool IsSeparator(char c) => c is '/' or '\\';

    /// <summary>C:\…, \\server\share: a Windows path (case doesn't matter, \ between folders).</summary>
    static bool IsWindows(string path) =>
        (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])) || path.StartsWith(@"\\", StringComparison.Ordinal);

    static char Separator(string start) => IsWindows(start) ? '\\' : '/';

    /// <summary>On this computer: its own separator (Windows also takes /, but \ is what it shows).</summary>
    static char LocalSeparator(string start) => OperatingSystem.IsWindows() || IsWindows(start) ? '\\' : '/';
}
