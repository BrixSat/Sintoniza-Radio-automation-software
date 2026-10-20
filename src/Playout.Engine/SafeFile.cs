namespace Playout.Engine;

/// <summary>Saves files so that a crash or power cut never leaves them half written.</summary>
public static class SafeFile
{
    /// <summary>
    /// Writes to a temporary file, flushes it to disk, then swaps it in. The previous version is kept as
    /// <c>file.bak</c>, for <see cref="ReadAllText"/> to fall back on.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, path);
    }

    /// <summary>The file's text if <paramref name="parse"/> accepts it, else the backup's, else null.</summary>
    public static T? Read<T>(string path, Func<string, T?> parse) where T : class
    {
        foreach (var file in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(file) && parse(File.ReadAllText(file)) is { } value) return value;
            }
            catch
            {
                // Damaged: try the backup.
            }
        }
        return null;
    }
}
