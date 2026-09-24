using System.Runtime.Versioning;

using Xunit.Sdk;

namespace CfSharp.SampleProvider.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class SamplePathSafetyTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.19041")]
    public void ShellRegistrationIdIsStableAndScopedToTheCurrentUser()
    {
        string first = ShellSyncRootRegistrar.GetRegistrationId();
        string second = ShellSyncRootRegistrar.GetRegistrationId();

        Assert.Equal(first, second);
        Assert.StartsWith("CfSharpSample!S-", first, StringComparison.Ordinal);
        Assert.EndsWith("!Default", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentsRequireAnExplicitCommandAndStateDatabase()
    {
        Assert.False(SampleArguments.TryParse([], out _, out _));
        Assert.False(
            SampleArguments.TryParse(
                ["run", "content", "root", "--once"],
                out _,
                out _));
    }

    [Fact]
    public void ArgumentsParseRunAndLifecycleCommands()
    {
        Assert.True(
            SampleArguments.TryParse(
                ["run", "content", "root", "--state-db", "state.db", "--once"],
                out SampleArguments? run,
                out _));
        Assert.Equal(SampleCommand.Run, run!.Command);
        Assert.True(run.RunOnce);
        Assert.Equal("state.db", run.StateDatabasePath);

        Assert.True(
            SampleArguments.TryParse(
                ["unregister", "root"],
                out SampleArguments? unregister,
                out _));
        Assert.Equal(SampleCommand.Unregister, unregister!.Command);
    }

    [Fact]
    public void ResolveContainedPathRejectsLexicalEscape()
    {
        using TemporaryDirectory root = new();

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.ResolveContainedPath(root.Path, Path.Combine(root.Path, "..", "outside")));
    }

    [Fact]
    public void ResolveContainedPathRejectsRootedOutsidePath()
    {
        using TemporaryDirectory root = new();
        string outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.ResolveContainedPath(root.Path, outside));
    }

    [Fact]
    public void ResolveContainedPathRejectsDirectoryReparsePoint()
    {
        using TemporaryDirectory root = new();
        using TemporaryDirectory outside = new();
        string link = Path.Combine(root.Path, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw SkipException.ForSkip(
                $"The test host cannot create a symbolic link: {exception.Message}");
        }
        catch (IOException exception) when (exception.HResult == unchecked((int)0x80070005))
        {
            throw SkipException.ForSkip(
                $"The test host cannot create a symbolic link: {exception.Message}");
        }

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.ResolveContainedPath(root.Path, Path.Combine(link, "file.txt")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CfSharp.SampleProvider.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
