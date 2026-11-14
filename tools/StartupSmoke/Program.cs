using Tempora.Tools.StartupSmoke;

try
{
    return args switch
    {
        ["test", .. var testArguments] => GodotTests.Run(testArguments),
        ["--command-worker", .. var commandArguments] => CapturedCommand.RunWorker(commandArguments),
        _ => StartupCheck.Run(args).Passed ? 0 : 1
    };
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
