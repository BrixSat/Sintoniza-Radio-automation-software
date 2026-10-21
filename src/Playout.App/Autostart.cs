using System.Runtime.Versioning;
using System.Security;

namespace Playout.App;

/// <summary>
/// Starts a program when the user logs in: the Run key on Windows, an XDG autostart entry on Linux, a LaunchAgent on
/// macOS. The setting lives there, not in the app's settings, so it is read back from the system.
/// </summary>
/// <param name="Name">Its name in the Run key (the playout keeps its first one, so an existing setting carries on).</param>
sealed class Autostart(string Name, string LinuxName, string MacLabel, string DisplayName, string Icon)
{
    /// <summary>The on-air program, Sintoniza Emi.</summary>
    public static readonly Autostart Playout = new("RadioPlayout", "radio-playout.desktop", "net.cesararaujo.radioplayout", AppInfo.Name, "radio-playout");
    /// <summary>The recorder, Sintoniza Rec.</summary>
    public static readonly Autostart Rec = new("SintonizaRec", "sintoniza-rec.desktop", "net.cesararaujo.sintonizarec", AppInfo.RecName, "radio-playout-rec");

    /// <summary>The program to start; null when run through "dotnet" (built from source), where this isn't offered.</summary>
    static string? Exe =>
        Environment.ProcessPath is { } exe && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? exe : null;

    public bool IsSupported => Exe != null && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

    string LinuxFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", LinuxName);

    string MacFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", MacLabel + ".plist");

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows()) return WindowsValue() != null;
                if (OperatingSystem.IsLinux()) return File.Exists(LinuxFile);
                if (OperatingSystem.IsMacOS()) return File.Exists(MacFile);
            }
            catch
            {
                // Can't tell: show it as off.
            }
            return false;
        }
    }

    /// <summary>
    /// When it is on: written again for the program as it is now (its executable was renamed, or moved by an update),
    /// so it still starts at login.
    /// </summary>
    public void Refresh()
    {
        try { if (Exe != null && IsEnabled) Write(true, Exe); }
        catch { /* left as it was */ }
    }

    /// <summary>Turns starting at login on or off. Throws with a readable message if the system refuses.</summary>
    public void Set(bool on)
    {
        if (on == IsEnabled) return;
        string exe = Exe ?? (on ? throw new InvalidOperationException(L.T("Starting at login is only available in an installed copy.")) : "");
        Write(on, exe);
    }

    void Write(bool on, string exe)
    {
        if (OperatingSystem.IsWindows()) SetWindows(on, exe);
        else if (OperatingSystem.IsLinux())
        {
            if (!on) { File.Delete(LinuxFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxFile)!);
            File.WriteAllText(LinuxFile, $"""
                [Desktop Entry]
                Type=Application
                Name={DisplayName}
                Exec="{exe}"
                Icon={Icon}
                X-GNOME-Autostart-enabled=true
                X-GNOME-Autostart-Delay=5

                """);
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (!on) { File.Delete(MacFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(MacFile)!);
            File.WriteAllText(MacFile, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key><string>{MacLabel}</string>
                    <key>ProgramArguments</key><array><string>{SecurityElement.Escape(exe)}</string></array>
                    <key>RunAtLoad</key><true/>
                </dict>
                </plist>

                """);
        }
    }

    [SupportedOSPlatform("windows")]
    string? WindowsValue()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) as string;
    }

    [SupportedOSPlatform("windows")]
    void SetWindows(bool on, string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{exe}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
