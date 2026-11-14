using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Tempora.Tools.StartupSmoke.Tests;

public sealed class StartupTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("Tempora startup tests ").FullName;

    [Fact]
    public void Fresh_and_restored_captures_stay_on_private_desktops()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = StartupCheck.Run([Probe.Executable]);
        try
        {
            Assert.True(result.Passed);
            foreach (string scenario in new[] { "fresh", "restored" })
            {
                var report = ReadReport(Path.Combine(result.Directory, scenario, "probe.json"));
                Assert.StartsWith("TemporaStartup-", report.Desktop);
                Assert.True(report.Visible);
                Assert.Equal(scenario == "restored", report.Restored);
            }
        }
        finally
        {
            Directory.Delete(result.Directory, true);
        }
    }

    [Fact]
    public void Show_window_captures_use_the_callers_desktop()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(directory, "result.json");
        Assert.Equal(0, DesktopProcess.Run(Probe.Start("--check-visible", path), false, TimeSpan.FromSeconds(20)));
        using var result = JsonDocument.Parse(File.ReadAllText(path));
        string captureDirectory = result.RootElement.GetProperty("Directory").GetString()!;
        try
        {
            Assert.True(result.RootElement.GetProperty("Passed").GetBoolean());
            foreach (string scenario in new[] { "fresh", "restored" })
            {
                var report = ReadReport(Path.Combine(captureDirectory, scenario, "probe.json"));
                Assert.Equal(result.RootElement.GetProperty("Desktop").GetString(), report.Desktop);
                Assert.True(report.Visible);
            }
        }
        finally
        {
            Directory.Delete(captureDirectory, true);
        }
    }

    [Fact]
    public void Isolated_children_preserve_arguments_environment_and_exit_code()
    {
        if (!OperatingSystem.IsWindows()) return;
        string reportPath = Path.Combine(directory, "quoted path.json");
        string value = "a \"quoted\" path\\";
        var start = Probe.Start("--report", reportPath, value, "");
        start.WorkingDirectory = directory;
        start.Environment["APPDATA"] = directory;
        Assert.Equal(7, DesktopProcess.Run(start, false, TimeSpan.FromSeconds(10)));
        var report = ReadReport(reportPath);
        Assert.StartsWith("TemporaStartup-", report.Desktop);
        Assert.Equal([value, ""], report.Arguments);
        Assert.Equal(directory, report.AppData);
        Assert.Equal(directory, report.WorkingDirectory);
    }

    [Fact]
    public void Captured_commands_preserve_output_and_isolation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string reportPath = Path.Combine(directory, "report.json");
        var start = Probe.Start("--report", reportPath, "a quoted \"argument\"", "");
        start.Environment["APPDATA"] = directory;
        start.WorkingDirectory = directory;
        Assert.Equal(7, CapturedCommand.Run(start, directory, TimeSpan.FromSeconds(10)));
        var report = ReadReport(reportPath);
        Assert.StartsWith("TemporaStartup-", report.Desktop);
        Assert.True(report.Visible);
        Assert.Equal(["a quoted \"argument\"", ""], report.Arguments);
        Assert.Equal(directory, report.AppData);
        Assert.Equal(directory, report.WorkingDirectory);
        Assert.Contains("probe output", File.ReadAllText(Path.Combine(directory, "stdout.log")));
        Assert.Contains("probe error", File.ReadAllText(Path.Combine(directory, "stderr.log")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Timed_out_commands_stop_their_child_processes(bool captured)
    {
        if (!OperatingSystem.IsWindows()) return;
        string reportPath = Path.Combine(directory, "child.txt");
        var start = Probe.Start("--wait", reportPath);
        int exitCode = captured ? CapturedCommand.Run(start, directory, TimeSpan.FromSeconds(3))
            : DesktopProcess.Run(start, false, TimeSpan.FromSeconds(3));
        Assert.Equal(-1, exitCode);
        int childId = int.Parse(File.ReadAllText(reportPath));
        try
        {
            using var child = Process.GetProcessById(childId);
            try
            {
                Assert.True(child.WaitForExit(5000));
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // The child has already exited and its process object was released.
        }
    }

    [Fact]
    public void A_missing_executable_reports_launch_failure()
    {
        if (!OperatingSystem.IsWindows()) return;
        var start = new ProcessStartInfo(Path.Combine(directory, "missing.exe")) { WorkingDirectory = directory };
        Assert.Throws<Win32Exception>(() => DesktopProcess.Run(start, false, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("error")]
    [InlineData("leak")]
    [InlineData("missing-frame")]
    [InlineData("empty-frame")]
    [InlineData("missing-log")]
    public void Incomplete_or_failed_captures_are_rejected(string failure)
    {
        long[] frames = Enumerable.Repeat(10L, failure == "missing-frame" ? 7 : 8).ToArray();
        if (failure == "empty-frame") frames[0] = 0;
        string log = failure switch
        {
            "error" => "SCRIPT ERROR: startup failed",
            "leak" => "WARNING: resources still in use",
            "missing-log" => "",
            _ => "Godot startup completed"
        };
        Assert.False(StartupCheck.Assess(failure == "exit" ? 7 : 0, frames, log).Passed);
    }

    [Fact]
    public void Unknown_options_are_rejected_before_launch() =>
        Assert.Throws<ArgumentException>(() => StartupCheck.Run([Probe.Executable, "--show-widnow"]));

    public void Dispose() => Directory.Delete(directory, true);

    private static Probe.Report ReadReport(string path) => JsonSerializer.Deserialize<Probe.Report>(File.ReadAllText(path))!;
}
