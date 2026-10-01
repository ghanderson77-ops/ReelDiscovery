using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using ReelDiscovery.Helpers;

namespace ReelDiscovery.Tests.Render;

internal sealed class RenderResult
{
    public string PngPath = string.Empty;
    public List<string> Clipping = new();
    public int DeviceDpi;
}

/// <summary>
/// Renders real WinForms controls at the display DPI (reference 144 = 150%; 120 = 125% also accepted) to PNG files under
/// renders/, and runs the clipping detector on the live control tree. Category=Render only.
/// </summary>
internal static class RenderHarness
{
    public const int RequiredDpi = 144;
    public const int MinimumDpi = 120;
    public static readonly Size Large = new(1100, 750);
    public static readonly Size Small = new(900, 600);

    // PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static readonly object InitLock = new();
    private static bool _initialized;

    /// <summary>
    /// True when this harness switched the process to PerMonitorV2; false when it was already set.
    /// </summary>
    public static bool DpiAwarenessSetByHarness { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    static RenderHarness()
    {
        EnsureInitialized();
    }

    public static string OutputFolder
    {
        get
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "renders");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
                return;
            _initialized = true;

            // Must run before any window is created; a false return means awareness was already set
            DpiAwarenessSetByHarness = SetProcessDpiAwarenessContext(PerMonitorAwareV2);
            Application.EnableVisualStyles();
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (InvalidOperationException)
            {
                // Already set by an earlier window in this test process; harmless
            }
        }
    }

    /// <summary>
    /// Runs the action on a fresh STA thread and rethrows any exception on the caller.
    /// </summary>
    public static void RunSta(Action action)
    {
        EnsureInitialized();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    public static RenderResult RenderControl(Func<Control> create, string name, Size logicalClientSize,
        Action<Control>? arrange = null, ClippingOptions? options = null)
    {
        var form = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            AutoScaleDimensions = new SizeF(7F, 15F),
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty,
            Opacity = 0,
            BackColor = Theme.Background
        };
        try
        {
            form.Show();
            CheckDpi(form);

            form.ClientSize = new Size(form.LogicalToDeviceUnits(logicalClientSize.Width),
                form.LogicalToDeviceUnits(logicalClientSize.Height));
            var content = new Panel
            {
                Name = "content",
                Dock = DockStyle.Fill,
                Padding = new Padding(form.LogicalToDeviceUnits(Theme.PageMargin), form.LogicalToDeviceUnits(Theme.PageMargin),
                    form.LogicalToDeviceUnits(Theme.PageMargin), 0),
                BackColor = Theme.Background
            };
            form.Controls.Add(content);

            var control = create();
            control.Dock = DockStyle.Fill;
            content.Controls.Add(control);
            Pump();

            arrange?.Invoke(control);
            for (var i = 0; i < 2; i++)
            {
                // Twice: late SizeChanged handlers re-layout after the first pass
                form.PerformLayout();
                Pump();
            }

            var fileName = $"{name}-{logicalClientSize.Width}x{logicalClientSize.Height}";
            return Capture(form, fileName, options);
        }
        finally
        {
            form.Close();
            form.Dispose();
        }
    }

    public static RenderResult RenderForm(Func<Form> create, string name, Action<Form>? arrange = null,
        ClippingOptions? options = null)
    {
        var form = create();
        try
        {
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
            form.Show();
            CheckDpi(form);
            Pump();

            arrange?.Invoke(form);
            for (var i = 0; i < 2; i++)
            {
                form.PerformLayout();
                Pump();
            }

            return Capture(form, name, options);
        }
        finally
        {
            form.Close();
            form.Dispose();
        }
    }

    private static void CheckDpi(Form form)
    {
        // Renders run at whatever scaling the display uses (the user switches between 125% and 150%);
        // 144 DPI (150%) remains the reference. Layouts are DPI-scaled, so a clean run at any scaled DPI is
        // meaningful. Below 120 DPI the per-monitor awareness most likely did not apply, so fail loudly
        // rather than pass silently (xUnit 2 cannot skip at runtime).
        var dpi = form.DeviceDpi;
        if (dpi < MinimumDpi)
        {
            throw new InvalidOperationException(
                $"Render tests need a scaled display (125% or 150%); this display is {dpi} DPI.");
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 25; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static RenderResult Capture(Form form, string fileName, ClippingOptions? options)
    {
        // DrawToBitmap paints the whole window (title bar and borders included), so bordered dialogs need
        // the full window size or their bottom and right edges are cut off; borderless hosts are unchanged
        var size = form.FormBorderStyle == FormBorderStyle.None ? form.ClientSize : form.Size;
        var png = Path.Combine(OutputFolder, fileName + ".png");
        using (var bitmap = new Bitmap(size.Width, size.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
            bitmap.Save(png, ImageFormat.Png);
        }

        var findings = ClippingDetector.Find(form, options);
        var report = findings.Count == 0 ? "no findings" : string.Join(Environment.NewLine, findings);
        File.WriteAllText(Path.Combine(OutputFolder, fileName + ".clip.txt"), report + Environment.NewLine);

        return new RenderResult { PngPath = png, Clipping = findings, DeviceDpi = form.DeviceDpi };
    }
}
