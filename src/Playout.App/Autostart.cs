using System.Runtime.Versioning;
using System.Security;

namespace Playout.App;

/// <summary>
/// Starts the app when the user logs in: the Run key on Windows, an XDG autostart entry on Linux, a LaunchAgent on
/// macOS. The setting lives there, not in the app's settings, so it is read back from the system.
/// </summary>
static class Autostart
{
    const string Name = "RadioPlayout";

    /// <summary>The program to start; null when run through "dotnet" (built from source), where this isn't offered.</summary>
    static string? Exe =>
        Environment.ProcessPath is { } exe && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? exe : null;

    public static bool IsSupported => Exe != null && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

    static string LinuxFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "radio-playout.desktop");

    static string MacFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "net.cesararaujo.radioplayout.plist");

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
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

    /// <summary>Turns starting at login on or off. Throws with a readable message if the system refuses.</summary>
    public static void Set(bool on)
    {
        if (on == IsEnabled) return;
        string exe = Exe ?? (on ? throw new InvalidOperationException("Starting at login is only available in an installed copy.") : "");

        if (OperatingSystem.IsWindows()) SetWindows(on, exe);
        else if (OperatingSystem.IsLinux())
        {
            if (!on) { File.Delete(LinuxFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxFile)!);
            File.WriteAllText(LinuxFile, $"""
                [Desktop Entry]
                Type=Application
                Name={AppInfo.Name}
                Exec="{exe}"
                Icon=radio-playout
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
                    <key>Label</key><string>net.cesararaujo.radioplayout</string>
                    <key>ProgramArguments</key><array><string>{SecurityElement.Escape(exe)}</string></array>
                    <key>RunAtLoad</key><true/>
                </dict>
                </plist>

                """);
        }
    }

    [SupportedOSPlatform("windows")]
    static string? WindowsValue()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) as string;
    }

    [SupportedOSPlatform("windows")]
    static void SetWindows(bool on, string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{exe}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
