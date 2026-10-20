using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playout.App;

/// <summary>A newer release on GitHub, and the package this installation can update itself with (null: download it by hand).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, UpdateAsset? Asset);

/// <param name="Sha256">Hex digest published by GitHub, if any; the download is checked against it.</param>
public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>Checks GitHub Releases for a newer version, downloads it and starts the installer.</summary>
static class Updates
{
    const string Repo = "BrixSat/Sintoniza-Radio-automation-software";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases";
    const string DebInstallDir = "/opt/radio-playout/"; // where the .deb and the .rpm put the app

    /// <summary>Installed from the .rpm (it adds this file), not the .deb.</summary>
    static bool FromRpm => File.Exists(Path.Combine(AppContext.BaseDirectory, "package-rpm"));

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPlayout", CurrentText));
        return http;
    }

    /// <summary>This build's version (set by build.sh; 0.0.0 for a build from source).</summary>
    public static Version Current { get; } = Normalise(typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The version as shown, e.g. "1.0.2" (without the commit hash .NET appends).</summary>
    public static string CurrentText { get; } =
        (typeof(Updates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Current.ToString())
        .Split('+')[0];

    /// <summary>Built from source rather than by build.sh: no version to compare with.</summary>
    public static bool IsDevBuild => Current == new Version(0, 0, 0);

    /// <summary>
    /// How this installation can update itself: Windows setup, Debian or RPM package, the macOS disk image for this Mac (an
    /// installed app), or not at all (zip/tar, from source).
    /// </summary>
    static string? PackageSuffix =>
        OperatingSystem.IsWindows() ? "-win-x64-setup.exe"
        : OperatingSystem.IsLinux() && AppContext.BaseDirectory.StartsWith(DebInstallDir) ? FromRpm ? ".x86_64.rpm" : "_amd64.deb"
        : OperatingSystem.IsMacOS() && AppContext.BaseDirectory.Contains(".app/Contents/MacOS")
            ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "-macos-arm64.dmg" : "-macos-x64.dmg"
        : null;

    /// <summary>macOS: installing opens the disk image, and the apps are dragged to Applications by hand.</summary>
    public static bool InstallOpensDiskImage => OperatingSystem.IsMacOS();

    /// <summary>The latest release if it is newer than this build, else null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        if (IsDevBuild) return null;
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var root = json.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version) || version <= Current) return null;

        UpdateAsset? asset = null;
        if (PackageSuffix is { } suffix)
        {
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                string? digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
                asset = new UpdateAsset(name, a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64(),
                    digest?.StartsWith("sha256:") == true ? digest["sha256:".Length..] : null);
                break;
            }
        }

        return new UpdateInfo(version, tag,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            root.GetProperty("html_url").GetString() ?? ReleasesPage, asset);
    }

    /// <summary>Downloads the package to a temporary folder, checking its SHA-256 when GitHub publishes one.</summary>
    public static async Task<string> DownloadAsync(UpdateAsset asset, IProgress<double> progress, CancellationToken cancel)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RadioPlayout-update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, asset.Name);

        using (var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? asset.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var target = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), cancel);
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        if (asset.Sha256 is { } expected)
        {
            await using var stream = File.OpenRead(file);
            string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancel));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException(L.T("The download is damaged (checksum mismatch). Try again later."));
            }
        }
        return file;
    }

    /// <summary>True when installing closes this app (Windows: the installer replaces the running files and restarts it).</summary>
    public static bool InstallClosesApp => OperatingSystem.IsWindows();

    /// <summary>
    /// Windows: starts the setup silently (it asks for admin rights, waits for this app to close and starts the new
    /// version); the caller then shuts down. Linux: installs the .deb or .rpm with pkexec (asks for the password) and waits.
    /// </summary>
    public static async Task InstallAsync(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(file, "/S /RESTART") { UseShellExecute = true });
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            // Finder opens the disk image: the new apps are dragged onto Applications, over the old ones.
            using var open = Process.Start(new ProcessStartInfo("open", [file])) ?? throw new InvalidOperationException(L.T("Could not open the disk image"));
            await open.WaitForExitAsync();
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("pkexec", LinuxInstall(file))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException(L.T("Could not start pkexec"));
        var errors = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        switch (process.ExitCode)
        {
            case 0: return;
            case 126 or 127: throw new OperationCanceledException(L.T("Installation cancelled (no administrator password given)."));
            default:
                string detail = (await errors).Trim().Split('\n').LastOrDefault(l => l.Length > 0) ?? $"exit code {process.ExitCode}";
                throw new InvalidOperationException(L.F("Installation failed: {0}", detail));
        }
    }

    /// <summary>The package manager's command to install the downloaded package (run through pkexec).</summary>
    static string[] LinuxInstall(string file)
    {
        if (!file.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)) return ["apt-get", "install", "-y", "--allow-downgrades", file];
        // Not signed: dnf and zypper are told to take it anyway (it was checked against its SHA-256).
        if (File.Exists("/usr/bin/dnf")) return ["/usr/bin/dnf", "install", "-y", "--nogpgcheck", file];
        if (File.Exists("/usr/bin/zypper")) return ["/usr/bin/zypper", "--non-interactive", "install", "--allow-unsigned-rpm", file];
        return ["rpm", "-U", "--oldpackage", "--replacepkgs", file];
    }

    /// <summary>Starts the newly installed version (Linux); the caller then shuts down.</summary>
    public static void Restart()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "RadioPlayout");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
    }

    public static void OpenInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser: nothing more we can do */ }
    }

    static bool TryParseVersion(string tag, out Version version)
    {
        string v = tag.TrimStart('v', 'V').Split('-', '+', '~')[0];
        if (Version.TryParse(v.Contains('.') ? v : v + ".0", out var parsed)) { version = Normalise(parsed); return true; }
        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>1.0 / 1.0.0 / 1.0.0.0 compare equal.</summary>
    static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
