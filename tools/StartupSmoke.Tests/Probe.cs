using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Tempora.Tools.StartupSmoke.Tests;

internal static class Probe
{
    internal static string Executable => Path.Combine(AppContext.BaseDirectory, "StartupSmoke.Tests.exe");

    internal sealed record Report(string Desktop, bool Visible, string[] Arguments, string? AppData, string WorkingDirectory, bool Restored);

    public static int Main(string[] args)
    {
        if (args[0] == "--sleep") return WaitForTermination();
        if (args[0] == "--wait") return WaitWithChild(args[1]);

        string desktop = DesktopProcess.CurrentDesktopName();
        if (!desktop.StartsWith("TemporaStartup-")) return 99;
        if (args[0] == "--check-visible") return CheckVisibleMode(args[1], desktop);
        return ReportWithVisibleWindow(args, desktop);
    }

    internal static ProcessStartInfo Start(params string[] args)
    {
        var start = new ProcessStartInfo(Executable) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        return start;
    }

    private static int WaitForTermination()
    {
        Thread.Sleep(TimeSpan.FromMinutes(1));
        return 0;
    }

    private static int WaitWithChild(string reportPath)
    {
        using var child = Process.Start(Start("--sleep"))!;
        File.WriteAllText(reportPath, child.Id.ToString());
        return WaitForTermination();
    }

    private static int CheckVisibleMode(string reportPath, string desktop)
    {
        var result = StartupCheck.Run([Executable, "--show-window"]);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { result.Directory, result.Passed, Desktop = desktop }));
        return result.Passed ? 0 : 1;
    }

    private static int ReportWithVisibleWindow(string[] args, string desktop)
    {
        IntPtr window = CreateWindowExW(0, "STATIC", "Startup probe", 0x10000000, 0, 0, 160, 80, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) return 98;
        try
        {
            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            bool restored = File.Exists(Path.Combine(appData!, "Godot/app_userdata/Tempora/autosave.tmpr"));
            var report = new Report(desktop, IsWindowVisible(window), args.Skip(2).ToArray(), appData, Environment.CurrentDirectory, restored);
            if (args[0] == "--report")
            {
                Console.WriteLine("probe output");
                Console.Error.WriteLine("probe error");
                File.WriteAllText(args[1], JsonSerializer.Serialize(report));
                return 7;
            }
            WriteStartupCapture(args, report);
            return 0;
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    private static void WriteStartupCapture(string[] args, Report report)
    {
        string directory = Path.GetDirectoryName(args[1])!;
        File.WriteAllText(Path.Combine(directory, "probe.json"), JsonSerializer.Serialize(report));
        File.WriteAllText(args[Array.IndexOf(args, "--log-file") + 1], "Godot startup completed");
        for (int i = 0; i < 8; i++) File.WriteAllText(Path.Combine(directory, $"frame{i:D8}.png"), "frame");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);
}
