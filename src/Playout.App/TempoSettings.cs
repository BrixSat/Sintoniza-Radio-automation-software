using System.Text.Json;

namespace Playout.App;

/// <summary>Sintoniza Tempo's settings, this computer's own (tempo.json in the settings folder): how the clock is shown.</summary>
public sealed class TempoSettings
{
    /// <summary>Full screen (a screen of its own in the studio).</summary>
    public bool FullScreen { get; set; }
    /// <summary>Where the window was (its top left, in pixels) and how big: on the screen it was on, full screen there too.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    /// <summary>The on-air program's red sign (MIC LIVE, ON AIR) over the clock, when Emi runs on this computer.</summary>
    public bool ShowSign { get; set; } = true;
    /// <summary>The countdown to the top of the hour under the date.</summary>
    public bool ShowTopOfHour { get; set; } = true;

    static string FilePath => Path.Combine(AppSettings.DataFolder, "tempo.json");

    public static TempoSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<TempoSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) { AppLog.Write($"{AppInfo.TempoName}: settings not saved: {ex.Message}"); }
    }
}
