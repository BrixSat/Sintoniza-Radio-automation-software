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

    public bool IsMySql => Provider.Equals("mysql", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the library is, for messages: the file, or user@server/database.</summary>
    public string Describe() => IsMySql ? $"{User}@{Server}:{Port}/{Database}" : SqliteFile;

    public string SqliteFile => string.IsNullOrWhiteSpace(File) ? LibraryDb.DefaultFile : File;

    public LibrarySettings Clone()
    {
        var c = (LibrarySettings)MemberwiseClone();
        c.Folders = Folders.ToList();
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
