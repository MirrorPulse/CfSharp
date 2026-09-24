internal enum SampleCommand
{
    Register,
    Run,
    Unregister,
}

internal sealed record SampleArguments(
    SampleCommand Command,
    string SyncRootPath,
    string? ContentRoot,
    string? StateDatabasePath,
    bool RunOnce)
{
    internal static bool TryParse(
        string[] args,
        out SampleArguments? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (args.Length == 0)
        {
            error = Usage;
            return false;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "register" when args.Length == 2:
                parsed = new SampleArguments(SampleCommand.Register, args[1], null, null, true);
                return true;
            case "unregister" when args.Length == 2:
                parsed = new SampleArguments(SampleCommand.Unregister, args[1], null, null, true);
                return true;
            case "run":
                return TryParseRun(args, out parsed, out error);
            default:
                error = Usage;
                return false;
        }
    }

    internal static string Usage => """
        Usage:
          CfSharp.SampleProvider register <sync-root-directory>
          CfSharp.SampleProvider run <content-directory> <sync-root-directory> --state-db <database-path> [--once]
          CfSharp.SampleProvider unregister <sync-root-directory>
        """;

    private static bool TryParseRun(
        string[] args,
        out SampleArguments? parsed,
        out string error)
    {
        parsed = null;
        error = string.Empty;
        if (args.Length < 5)
        {
            error = Usage;
            return false;
        }

        string? stateDatabasePath = null;
        bool runOnce = false;
        for (int index = 3; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--state-db" when index + 1 < args.Length && stateDatabasePath is null:
                    stateDatabasePath = args[++index];
                    break;
                case "--once" when !runOnce:
                    runOnce = true;
                    break;
                default:
                    error = Usage;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(stateDatabasePath))
        {
            error = Usage;
            return false;
        }

        parsed = new SampleArguments(
            SampleCommand.Run,
            args[2],
            args[1],
            stateDatabasePath,
            runOnce);
        return true;
    }
}
