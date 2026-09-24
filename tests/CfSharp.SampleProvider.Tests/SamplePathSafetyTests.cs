using System.Diagnostics;
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
    [SupportedOSPlatform("windows10.0.19041")]
    public void ShellRegistrationLookupIsSafeWhenTheSampleIsNotRegistered()
    {
        if (ShellSyncRootRegistrar.TryGetRegisteredPath(out string? path))
        {
            Assert.False(string.IsNullOrWhiteSpace(path));
        }
        else
        {
            Assert.Null(path);
        }
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
    public void EnumerationProcessArgumentsAreBuiltBeforeTheProcessStarts()
    {
        ProcessStartInfo startInfo = global::SampleProvider.CreateEnumerationProcessStartInfo(
            @"C:\CfSharpAcceptance\sync-root");

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(
            [
                "/d",
                "/c",
                "dir",
                "/s",
                "/b",
                @"C:\CfSharpAcceptance\sync-root",
            ],
            startInfo.ArgumentList);
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

    [Fact]
    public void ResolveSyncRootCallbackPathMapsRootRelativeCallbacksToTheSyncRootVolume()
    {
        using TemporaryDirectory syncRoot = new();
        string relativeToVolume = Path.GetRelativePath(
            Path.GetPathRoot(syncRoot.Path)!,
            syncRoot.Path);
        string callbackPath = Path.DirectorySeparatorChar +
            relativeToVolume.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        string resolved = SamplePathSafety.ResolveSyncRootCallbackPath(
            syncRoot.Path,
            callbackPath);

        Assert.Equal(syncRoot.Path, resolved, ignoreCase: true);
    }

    [Fact]
    public void ResolveSyncRootCallbackPathRejectsAbsolutePathOutsideTheSyncRoot()
    {
        using TemporaryDirectory syncRoot = new();
        string outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.ResolveSyncRootCallbackPath(syncRoot.Path, outside));
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
