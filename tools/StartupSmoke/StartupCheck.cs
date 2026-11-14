using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Tempora.Tools.StartupSmoke;

internal static class StartupCheck
{
    internal sealed record Result(string Directory, bool Passed);
    internal sealed record Assessment(bool Passed, string[] Errors);

    internal static Result Run(string[] args)
    {
        string[] paths = args.Where(argument => argument != "--show-window").ToArray();
        if (paths.Length != 1 || paths[0].StartsWith("--"))
            throw new ArgumentException("Usage: StartupSmoke <executable> [--show-window]");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Startup capture requires Windows.");
        string executable = Path.GetFullPath(paths[0]);
        if (!File.Exists(executable)) throw new FileNotFoundException("Export executable not found.", executable);
        string directory = Directory.CreateTempSubdirectory("tempora-startup-smoke-").FullName;
        Console.WriteLine($"Startup screenshots and logs: {directory}");
        bool showWindow = args.Contains("--show-window");
        bool fresh = RunScenario(executable, Path.Combine(directory, "fresh"), false, showWindow);
        bool restored = RunScenario(executable, Path.Combine(directory, "restored"), true, showWindow);
        Console.WriteLine("Inspect frame00000000.png and frame00000007.png in each scenario for visual correctness; images are not compared automatically.");
        return new Result(directory, fresh && restored);
    }

    internal static Assessment Assess(int exitCode, long[] frameSizes, string log)
    {
        string[] errors = log.Split('\n').Where(line => Regex.IsMatch(line,
            "^(ERROR:|SCRIPT ERROR:|Unhandled exception)|WARNING:.*(leaked|still in use|orphan)", RegexOptions.IgnoreCase)).ToArray();
        bool passed = exitCode == 0 && frameSizes.Length == 8 && frameSizes.All(size => size > 0) && log.Length > 0 && errors.Length == 0;
        return new Assessment(passed, errors);
    }

    private static bool RunScenario(string executable, string directory, bool restored, bool showWindow)
    {
        PrepareProfile(directory, restored);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.Environment["APPDATA"] = Path.Combine(directory, "profile");
        foreach (string argument in new[] { "--write-movie", Path.Combine(directory, "frame.png"), "--fixed-fps", "2",
            "--quit-after", "8", "--resolution", "1280x720", "--log-file", Path.Combine(directory, "run.log") })
            start.ArgumentList.Add(argument);
        int exitCode = DesktopProcess.Run(start, showWindow, TimeSpan.FromSeconds(60));
        if (exitCode == -1) Console.Error.WriteLine("Startup exceeded 60 seconds.");
        return CheckCapture(directory, exitCode);
    }

    private static void PrepareProfile(string directory, bool restored)
    {
        string userData = Path.Combine(directory, "profile/Godot/app_userdata/Tempora");
        Directory.CreateDirectory(userData);
        if (!restored) return;
        string audio = Path.Combine(directory, "Loop.mp3");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Loop.mp3"), audio);
        File.WriteAllText(Path.Combine(userData, "settings.cfg"), "[settings]\nNumberOfRows=3\nTextScaleFactor=2.0\n");
        File.WriteAllText(Path.Combine(userData, "autosave.tmpr"), $"[AudioPath]\n{audio}\n[TimeSignaturePoints]\n[TimingPoints]\n0;0;4;4;0.5\n");
    }

    private static bool CheckCapture(string directory, int exitCode)
    {
        long[] frames = Directory.GetFiles(directory, "frame*.png").Select(path => new FileInfo(path).Length).ToArray();
        string logPath = Path.Combine(directory, "run.log");
        string log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
        var assessment = Assess(exitCode, frames, log);
        Console.WriteLine(assessment.Passed
            ? $"PASS {directory}: startup and capture completed without runtime errors."
            : $"FAIL {directory}: exit={exitCode}, frames={frames.Length}, errors={assessment.Errors.Length}.");
        foreach (string error in assessment.Errors) Console.Error.WriteLine(error);
        return assessment.Passed;
    }
}