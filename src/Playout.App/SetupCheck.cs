using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Playout.App;

/// <summary>Something risky in how this computer is set up for being on air: a short label (for the STUDIO lamp) and what to do.</summary>
public sealed record SetupWarning(string Short, string Text);

/// <summary>
/// Looks at how this computer is set up for being on air (every minute, off the screen): on Wi-Fi with no cable (a
/// share or the database dropping for a few seconds), on battery (the power cable out), and on Windows a Wi-Fi adapter
/// Windows may switch off to save power. Said on the STUDIO lamp, in the log and in Sintoniza Pro.
/// </summary>
public static class SetupCheck
{
    public static List<SetupWarning> Now()
    {
        var warnings = new List<SetupWarning>();
        try
        {
            if (WifiOnly() is { } wifi)
            {
                warnings.Add(new SetupWarning(L.T("Wi-Fi"), L.T("On Wi-Fi with no network cable: a few seconds without the network can stop the music from a share and the database. On air, a cable is best.")));
                if (OperatingSystem.IsWindows() && MaySleep(wifi))
                    warnings.Add(new SetupWarning(L.T("Wi-Fi"), L.T("Windows may switch the Wi-Fi off to save power: Device Manager → Network adapters → the Wi-Fi adapter → Power Management → untick \"Allow the computer to turn off this device to save power\".")));
            }
        }
        catch (Exception) { /* the network can't be looked at: nothing said */ }
        try
        {
            if (OnBattery())
                warnings.Add(new SetupWarning(L.T("Battery"), L.T("On battery: the power cable is out. The station goes off air when the battery runs out.")));
        }
        catch (Exception) { /* not known */ }
        return warnings;
    }

    /// <summary>The Wi-Fi adapter this computer reaches the network through, when no wired one has a way out (a gateway).</summary>
    static NetworkInterface? WifiOnly()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
            .ToList();
        var wifi = up.FirstOrDefault(Wireless);
        // A virtual adapter (Hyper-V's switch over the Wi-Fi, a VPN) isn't a cable.
        return wifi != null && !up.Any(n => !Wireless(n) && !Virtual(n)) ? wifi : null;
    }

    static bool Virtual(NetworkInterface n) =>
        new[] { "vEthernet", "docker", "virbr", "vmnet", "veth", "br-", "tun", "tap", "wg", "tailscale", "zt" }.Any(name => n.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        || new[] { "Hyper-V", "Virtual", "VPN", "TAP-Windows", "WireGuard", "Tailscale", "ZeroTier", "VMware", "VirtualBox" }
            .Any(word => n.Description.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>A Wi-Fi adapter (Linux says so in /sys: .NET calls them Ethernet there).</summary>
    static bool Wireless(NetworkInterface n) =>
        n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
        || OperatingSystem.IsLinux() && (Directory.Exists($"/sys/class/net/{n.Name}/wireless") || Directory.Exists($"/sys/class/net/{n.Name}/phy80211"));

    /// <summary>Windows: the adapter's "Allow the computer to turn off this device to save power" is on (PnPCapabilities without 0x18).</summary>
    [SupportedOSPlatform("windows")]
    static bool MaySleep(NetworkInterface adapter)
    {
        using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
        if (adapters == null) return false;
        foreach (var name in adapters.GetSubKeyNames())
        {
            using var key = adapters.OpenSubKey(name);
            if (key?.GetValue("NetCfgInstanceId") is not string id || !id.Equals(adapter.Id, StringComparison.OrdinalIgnoreCase)) continue;
            // Only when the setting is there and allows it: on most laptops of today (Modern Standby) it isn't, and the
            // Power Management tab it points to doesn't exist.
            return key.GetValue("PnPCapabilities") is int caps && (caps & 0x18) != 0x18;
        }
        return false;
    }

    static bool OnBattery()
    {
        if (OperatingSystem.IsWindows())
            return GetSystemPowerStatus(out var status) && status.ACLineStatus == 0 && status.BatteryFlag != 128;
        if (OperatingSystem.IsLinux())
        {
            const string root = "/sys/class/power_supply";
            if (!Directory.Exists(root)) return false;
            var supplies = Directory.GetDirectories(root).Select(d => (Type: Read(Path.Combine(d, "type")), Online: Read(Path.Combine(d, "online")))).ToList();
            // A mains supply that says it is off, and a battery: on battery (a desktop has neither, or mains on).
            return supplies.Any(s => s.Type == "Battery") && supplies.Where(s => s.Type == "Mains").DefaultIfEmpty().All(s => s.Online == "0");
        }
        if (OperatingSystem.IsMacOS())
        {
            using var pmset = Process.Start(new ProcessStartInfo("pmset", ["-g", "batt"]) { RedirectStandardOutput = true, UseShellExecute = false });
            if (pmset == null) return false;
            var output = pmset.StandardOutput.ReadToEndAsync();
            if (!pmset.WaitForExit(3000) || !output.Wait(1000))
            {
                try { pmset.Kill(); } catch (Exception) { /* gone */ }
                return false;
            }
            return output.Result.Contains("'Battery Power'", StringComparison.Ordinal);
        }
        return false;
    }

    static string Read(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file).Trim() : ""; }
        catch (Exception) { return ""; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
