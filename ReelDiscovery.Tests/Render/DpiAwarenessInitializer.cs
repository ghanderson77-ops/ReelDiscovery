using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Makes the test process per-monitor DPI aware as soon as the test assembly loads, before any test
/// creates a window. DPI awareness can only be set once per process: if a non-render test creates a
/// WinForms window first (a full, unfiltered run such as Visual Studio's Test Explorer), the process is
/// locked as DPI-unaware and every render reports 96 DPI.
/// </summary>
internal static class DpiAwarenessInitializer
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // CA2255 is about library initializers running in consumers' processes; this assembly is a test host only
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        // False when awareness was already set (e.g. by the host); the harness reports the actual DPI either way
        SetProcessDpiAwarenessContext(PerMonitorAwareV2);
    }
}
