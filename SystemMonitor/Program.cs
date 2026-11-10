using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SystemMonitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Необработанная ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => MessageBox.Show(e.ExceptionObject?.ToString() ?? "unknown", "Критическая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

        if (args.Length >= 2 && args[0] == "--selftest")
        {
            SelfTest(
                args[1],
                args.Length > 2 ? args[2] : null,
                args.Length > 3 ? args[3] : null,
                args.Length > 4 ? args[4] : null);
            return;
        }

        if (args.Length >= 2 && args[0] == "--shots")
        {
            Shots(args[1]);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void SelfTest(string logPath, string themeId, string densityId, string layoutId)
    {
        try
        {
            using var form = new MainForm();
            if (themeId != null || densityId != null || layoutId != null)
            {
                form.ApplyInterfaceForTest(themeId ?? "graphite", densityId ?? "normal", layoutId ?? "full");
            }

            ShowForCapture(form);
            Pump(6000);

            File.WriteAllText(logPath, "OK rows=" + form.ViewCount + " apps=" + form.AppCount + " heavy=" + form.HeavyCount + Environment.NewLine + form.DebugSnapshot());
            SaveDraw(form, logPath + ".png");
            TryScreenCapture(form, logPath + ".screen.png");
            form.Close();
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(logPath, "FAIL " + ex); } catch { }
        }
    }

    private static void Shots(string directory)
    {
        Directory.CreateDirectory(directory);
        string logPath = Path.Combine(directory, "shots.log");
        try { File.Delete(logPath); } catch { }

        using var form = new MainForm();
        form.ApplyInterfaceForTest("graphite", "normal", "full");
        ShowForCapture(form);
        Pump(5000);

        var variants = new (string Theme, string Density, string Layout, string Name)[]
        {
            ("graphite", "normal", "full", "01-graphite-full"),
            ("light", "normal", "full", "02-light-full"),
            ("midnight", "normal", "full", "03-midnight-full"),
            ("contrast", "normal", "full", "04-contrast-full"),
            ("light", "compact", "nocharts", "05-light-compact"),
            ("graphite", "spacious", "table", "06-graphite-table"),
            ("light", "normal", "dashboard", "07-light-dashboard"),
        };

        foreach (var variant in variants)
        {
            // One variant goes through the deferred path used by real menu clicks.
            if (variant.Name == "02-light-full")
            {
                form.SelectInterfaceForTest(variant.Theme, variant.Density, variant.Layout);
            }
            else
            {
                form.ApplyInterfaceForTest(variant.Theme, variant.Density, variant.Layout);
            }
            form.Activate();
            Pump(1800);
            string state = form.DebugSnapshot().Split(Environment.NewLine)[0];
            File.AppendAllText(logPath, $"{variant.Name}: {state}{Environment.NewLine}");
            if (!TryScreenCapture(form, Path.Combine(directory, variant.Name + ".png")))
            {
                SaveDraw(form, Path.Combine(directory, variant.Name + ".png"));
            }
        }

        form.ApplyInterfaceForTest("light", "normal", "full");
        form.Activate();
        Pump(1200);
        form.ShowInterfaceMenuForTest();
        Pump(500);
        TryScreenCapture(form, Path.Combine(directory, "08-menu-light.png"));
        form.HideInterfaceMenuForTest();
        Pump(300);

        form.SelectInterfaceForTest("graphite", "normal", "full");
        form.Activate();
        Pump(1000);
        File.AppendAllText(logPath, $"09-menu-dark: {form.DebugSnapshot().Split(Environment.NewLine)[0]}{Environment.NewLine}");
        form.ShowInterfaceMenuForTest(openThemeSubmenu: true);
        Pump(500);
        TryScreenCapture(form, Path.Combine(directory, "09-menu-dark.png"));
        form.HideInterfaceMenuForTest();

        form.Close();
    }

    private static void ShowForCapture(Form form)
    {
        form.Show();
        form.Activate();
        form.BringToFront();
        form.TopMost = true;
        Application.DoEvents();
    }

    private static void Pump(int milliseconds)
    {
        long until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Application.DoEvents();
            Thread.Sleep(40);
        }
    }

    private static void SaveDraw(Form form, string path)
    {
        try
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch { }
    }

    private static bool TryScreenCapture(Form form, string path)
    {
        try
        {
            Rectangle bounds = VisibleBounds(form);
            if (bounds.Width <= 0 || bounds.Height <= 0) return false;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out DwmRect rect, int size);

    /// <summary>Visible window rectangle without the invisible resize border included in Form.Bounds.</summary>
    private static Rectangle VisibleBounds(Form form)
    {
        try
        {
            if (DwmGetWindowAttribute(form.Handle, 9, out DwmRect rect, Marshal.SizeOf<DwmRect>()) == 0)
            {
                var result = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (result.Width > 0 && result.Height > 0) return result;
            }
        }
        catch { }
        return form.Bounds;
    }
}
