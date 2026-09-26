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

        Assert.True(
            SampleArguments.TryParse(
                ["enumerate", "root"],
                out SampleArguments? enumerate,
                out _));
        Assert.Equal(SampleCommand.Enumerate, enumerate!.Command);
    }

    [Fact]
    public void ContentComparisonRunsInAnIndependentSystemProcess()
    {
        ProcessStartInfo startInfo = global::SampleProvider.CreateContentComparisonProcessStartInfo(
            @"C:\Sample\content\source.bin",
            @"C:\Sample\sync-root\source.bin");

        Assert.Equal(
            Path.Combine(Environment.SystemDirectory, "fc.exe"),
            startInfo.FileName,
            ignoreCase: true);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(
            [
                "/b",
                "/offline",
                @"C:\Sample\content\source.bin",
                @"C:\Sample\sync-root\source.bin",
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task ContentComparisonTreatsShellMetacharactersAsFileNames()
    {
        using TemporaryDirectory root = new();
        string source = Path.Combine(root.Path, "a&b.txt");
        string placeholder = Path.Combine(root.Path, "copy&b.txt");
        await File.WriteAllTextAsync(source, "same");
        await File.WriteAllTextAsync(placeholder, "same");

        using Process comparison = Process.Start(
            global::SampleProvider.CreateContentComparisonProcessStartInfo(source, placeholder))!;
        await comparison.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, comparison.ExitCode);
    }

    [Fact]
    public void ContentComparisonKeepsSourceAndPlaceholderAsSeparateArguments()
    {
        ProcessStartInfo startInfo = global::SampleProvider.CreateContentComparisonProcessStartInfo(
            @"C:\Sample\content",
            @"C:\Sample\sync-root");

        Assert.Equal(@"C:\Sample\content", startInfo.ArgumentList[^2]);
        Assert.Equal(@"C:\Sample\sync-root", startInfo.ArgumentList[^1]);
    }

    [Fact]
    public void EnumerationConsumerUsesDirectProcessArguments()
    {
        ProcessStartInfo startInfo = global::SampleProvider.CreateEnumerationProcessStartInfo(
            @"C:\CfSharpAcceptance\sync-root");

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Contains("enumerate", startInfo.ArgumentList, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(@"C:\CfSharpAcceptance\sync-root", startInfo.ArgumentList[^1]);
        Assert.DoesNotContain("/c", startInfo.ArgumentList, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyTheKnownMissingCloudRootStateIsTreatedAsStale()
    {
        CloudFilesException stale = CloudFilesException.FromHResult(
            "CloudSyncRoot.GetInfo",
            "C:\\CfSharpAcceptance\\sync-root",
            unchecked((int)0x80070186));
        CloudFilesException accessDenied = CloudFilesException.FromHResult(
            "CloudSyncRoot.GetInfo",
            "C:\\CfSharpAcceptance\\sync-root",
            unchecked((int)0x80070005));
        CloudFilesException differentOperation = CloudFilesException.FromHResult(
            "CloudSyncRoot.Unregister",
            "C:\\CfSharpAcceptance\\sync-root",
            unchecked((int)0x80070186));

        Assert.True(global::SampleProvider.IsStaleCloudRootRegistration(stale));
        Assert.False(global::SampleProvider.IsStaleCloudRootRegistration(accessDenied));
        Assert.False(global::SampleProvider.IsStaleCloudRootRegistration(differentOperation));
    }

    [Fact]
    public void ResolveContainedPathRejectsLexicalEscape()
    {
        using TemporaryDirectory root = new();

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.ResolveContainedPath(root.Path, Path.Combine(root.Path, "..", "outside")));
    }

    [Fact]
    public void SyncRootValidationRejectsProtectedLocations()
    {
        string volumeRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.NormalizeSyncRootPath(volumeRoot, "sync-root"));

        string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.NormalizeSyncRootPath(windowsRoot, "sync-root"));
    }

    [Fact]
    public void DisjointPathValidationRejectsNestedRoots()
    {
        using TemporaryDirectory root = new();
        string child = Path.Combine(root.Path, "child");

        Assert.Throws<InvalidDataException>(() =>
            SamplePathSafety.EnsureDisjointPaths(root.Path, "content", child, "sync-root"));
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
