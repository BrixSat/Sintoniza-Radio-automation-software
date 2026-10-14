namespace Playout.App;

/// <summary>
/// One text file per day in the data folder's <c>logs</c> folder: what went on air, warnings, errors, crashes and
/// restarts, to find out afterwards what happened overnight. Files older than <see cref="KeepDays"/> are deleted.
/// </summary>
static class AppLog
{
    const int KeepDays = 90;
    static readonly object Lock = new();

    public static string Folder => Path.Combine(AppSettings.DataFolder, "logs");

    /// <summary>Adds a line; a time of day is put in front unless it starts with one (engine messages do).</summary>
    public static void Write(string message)
    {
        var now = DateTime.Now;
        string line = message.Length >= 8 && TimeOnly.TryParseExact(message[..8], "HH:mm:ss", out _)
            ? message
            : $"{now:HH:mm:ss}  {message}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"{now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch
            {
                // The log must never take the station down.
            }
        }
    }

    /// <summary>Deletes day files older than <see cref="KeepDays"/>.</summary>
    public static void CleanUp()
    {
        try
        {
            var oldest = DateTime.Today.AddDays(-KeepDays);
            foreach (var file in Directory.EnumerateFiles(Folder, "*.log"))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var day) && day < oldest)
                    File.Delete(file);
        }
        catch
        {
            // Best effort.
        }
    }
}
