using System.Diagnostics;
using System.Xml.Linq;

namespace Tempora.Tools.StartupSmoke;

internal static class GodotTests
{
    internal static int Run(string[] args)
    {
        bool native = args.ElementAtOrDefault(1) == "--native";
        int separator = native ? 2 : 1;
        if (args.Length == 0 || (args.Length > separator && args[separator] != "--"))
            throw new ArgumentException("Usage: StartupSmoke test <godot-executable> [--native] [-- <dotnet-test-arguments>]");
        string godot = Path.GetFullPath(args[0]);
        if (!File.Exists(godot)) throw new FileNotFoundException("Godot executable not found.", godot);

        string directory = Directory.CreateTempSubdirectory("tempora-tests-").FullName;
        Console.WriteLine($"Test logs, settings and captures: {directory}");
        var command = PrepareCommand(godot, native, args.Skip(separator + 1), directory);
        int exitCode = RunIsolated(command, directory);
        ReplayLog(Path.Combine(directory, "stdout.log"), Console.Out);
        ReplayLog(Path.Combine(directory, "stderr.log"), Console.Error);
        if (exitCode == -1) Console.Error.WriteLine("Godot tests exceeded five minutes; the process tree was stopped.");
        return exitCode;
    }

    private static ProcessStartInfo PrepareCommand(string godot, bool native, IEnumerable<string> arguments, string directory)
    {
        string settingsPath = WriteTestSettings(native, directory);
        var command = new ProcessStartInfo("dotnet") { WorkingDirectory = Environment.CurrentDirectory };
        command.Environment["APPDATA"] = Path.Combine(directory, "profile");
        foreach (string argument in new[] { "test", "Tempora.csproj", "--settings", settingsPath, "-e", $"GODOT_BIN={godot}" }.Concat(arguments))
            command.ArgumentList.Add(argument);
        return command;
    }

    private static string WriteTestSettings(bool native, string directory)
    {
        var settings = XDocument.Load(".runsettings");
        if (native)
        {
            XElement parameters = settings.Root!.Element("GdUnit4")!.Element("Parameters")!;
            parameters.Value = parameters.Value.Replace("--headless", "--rendering-method gl_compatibility");
        }
        string settingsPath = Path.Combine(directory, "tests.runsettings");
        settings.Save(settingsPath);

        return settingsPath;
    }

    private static int RunIsolated(ProcessStartInfo command, string directory)
    {
        // Separate settings profiles still share the project's .godot imports and Debug build output.
        using var mutex = new Mutex(false, "TemporaRefactorGodotTests");
        mutex.WaitOne();
        try
        {
            return CapturedCommand.Run(command, directory, TimeSpan.FromMinutes(5));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void ReplayLog(string path, TextWriter output)
    {
        if (File.Exists(path)) output.Write(File.ReadAllText(path));
    }
}
