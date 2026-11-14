using System.Diagnostics;

namespace Tempora.Tools.StartupSmoke;

internal static class CapturedCommand
{
    internal static int Run(ProcessStartInfo command, string directory, TimeSpan timeout)
    {
        var worker = new ProcessStartInfo(Path.ChangeExtension(typeof(CapturedCommand).Assembly.Location, ".exe"))
        {
            WorkingDirectory = command.WorkingDirectory
        };
        foreach (string argument in new[] { "--command-worker", directory, command.FileName }.Concat(command.ArgumentList))
            worker.ArgumentList.Add(argument);

        worker.Environment.Clear();
        foreach (var (name, value) in command.Environment) worker.Environment[name] = value;

        return DesktopProcess.Run(worker, false, timeout);
    }

    internal static int RunWorker(string[] args)
    {
        using var output = new FileStream(Path.Combine(args[0], "stdout.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
        using var error = new FileStream(Path.Combine(args[0], "stderr.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            var start = new ProcessStartInfo(args[1])
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string argument in args.Skip(2)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Task copying = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output), process.StandardError.BaseStream.CopyToAsync(error));
            process.WaitForExit();
            copying.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Exception exception)
        {
            using var writer = new StreamWriter(error);
            writer.WriteLine(exception);
            return 1;
        }
    }
}
