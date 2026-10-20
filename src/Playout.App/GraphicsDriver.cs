using System.Runtime.InteropServices;

namespace Playout.App;

/// <summary>
/// The NVIDIA driver on Linux spins a CPU core while it waits for the screen to refresh: the window's drawing thread
/// used most of a core all the time. Telling it to sleep instead (__GL_YIELD=USLEEP) frees it. It has to be in the
/// process environment before the driver is loaded, and .NET keeps its own copy of the environment, so it is set
/// through the C library. Nothing is changed when it was set already (to try another value).
/// </summary>
static class GraphicsDriver
{
    public static readonly (string Name, string Value)[] Environment = [("__GL_YIELD", "USLEEP")];

    public static void Prepare()
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var (name, value) in Environment)
        {
            if (System.Environment.GetEnvironmentVariable(name) != null) continue;
            System.Environment.SetEnvironmentVariable(name, value); // child processes started from .NET
            try { setenv(name, value, 0); } catch (Exception) { /* no libc: nothing to do */ }
        }
    }

    [DllImport("libc", SetLastError = true)]
    static extern int setenv(string name, string value, int overwrite);
}
