using Playout.Engine;
using System.Text.Json;

namespace Playout.Library;

/// <summary>
/// Which library database to use and which folders to scan into it. Kept in the app's settings.json under
/// "Library", where radio-import also finds it, so both use the same library unless told otherwise.
/// </summary>
public sealed class LibrarySettings
{
    /// <summary>sqlite (a file on this computer) or mysql (a MySQL / MariaDB server, shared by several studios).</summary>
    public string Provider { get; set; } = "sqlite";
    /// <summary>SQLite file; empty = <see cref="LibraryDb.DefaultFile"/>.</summary>
    public string File { get; set; } = "";
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Music folders scanned into the library ("Scan now", radio-import without folders).</summary>
    public List<string> Folders { get; set; } = new();
    /// <summary>Folders whose songs go in a category (folder → category id): new songs found there get it after each scan.</summary>
    public Dictionary<string, long> FolderCategories { get; set; } = new();
    /// <summary>The app scans the folders this often (minutes; 0 = only when asked). Each scan only reads what changed.</summary>
    public int ScanEveryMinutes { get; set; } = 60;
    /// <summary>The app also scans shortly after files in the folders are added, changed or deleted.</summary>
    public bool WatchFolders { get; set; } = true;
    /// <summary>
    /// Where the library's music is on this computer when it sees it under another path (a share: /mnt/radio in the
    /// library, Z:\ here). Empty: the library's paths are this computer's. See <see cref="SharedPaths"/>.
    /// </summary>
    public List<PathMapping> PathMap { get; set; } = new();

    /// <summary>Makes <see cref="PathMap"/> the one this program uses.</summary>
    public void ApplyPathMap() => SharedPaths.Set(PathMap.Select(m => new SharedPaths.Mapping(m.Library, m.Local)));

    public bool IsMySql => Provider.Equals("mysql", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the library is, for messages: the file, or user@server/database.</summary>
    public string Describe() => IsMySql ? $"{User}@{Server}:{Port}/{Database}" : SqliteFile;

    /// <summary>Changes only when the library to open changes (not with the folders or scan options).</summary>
    public string ConnectionKey() => IsMySql ? $"mysql|{Server}|{Port}|{Database}|{User}|{Password}" : $"sqlite|{SqliteFile}";

    public string SqliteFile => string.IsNullOrWhiteSpace(File) ? LibraryDb.DefaultFile : File;

    public LibrarySettings Clone()
    {
        var c = (LibrarySettings)MemberwiseClone();
        c.Folders = Folders.ToList();
        c.PathMap = PathMap.Select(m => new PathMapping { Library = m.Library, Local = m.Local }).ToList();
        return c;
    }

    /// <summary>The library settings the app saved (settings.json), or the defaults.</summary>
    public static LibrarySettings FromApp()
    {
        string file = Path.Combine(Path.GetDirectoryName(LibraryDb.DefaultFile)!, "settings.json");
        try
        {
            using var json = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("Library", out var library))
                return library.Deserialize<LibrarySettings>() ?? new();
        }
        catch
        {
            // No app settings (yet): the defaults.
        }
        return new();
    }
}

/// <summary>A start of the library's paths (/mnt/radio) and where that is on this computer (Z:\\).</summary>
public sealed class PathMapping
{
    public string Library { get; set; } = "";
    public string Local { get; set; } = "";
}
