internal static class SamplePathSafety
{
    internal static string NormalizeExistingDirectory(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);

        string fullPath = NormalizeFullPath(path, parameterName);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The {parameterName} directory does not exist: {fullPath}");
        }

        RejectReparsePoints(fullPath);
        return fullPath;
    }

    internal static string ResolveContainedPath(string rootPath, string candidatePath)
    {
        string root = Path.TrimEndingDirectorySeparator(NormalizeFullPath(rootPath, nameof(rootPath)));
        string candidate = NormalizeFullPath(candidatePath, nameof(candidatePath));
        if (!IsSameOrChild(root, candidate))
        {
            throw new InvalidDataException(
                $"The path '{candidate}' escapes the content directory '{root}'.");
        }

        RejectReparsePoints(candidate);
        return candidate;
    }

    internal static string ResolveSyncRootCallbackPath(string syncRootPath, string normalizedPath)
    {
        string syncRoot = Path.TrimEndingDirectorySeparator(
            NormalizeFullPath(syncRootPath, nameof(syncRootPath)));
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            (normalizedPath.Length == 1 &&
                (normalizedPath[0] == Path.DirectorySeparatorChar ||
                    normalizedPath[0] == Path.AltDirectorySeparatorChar)))
        {
            return syncRoot;
        }

        string rootPrefix = syncRoot + Path.DirectorySeparatorChar;
        string candidate;
        if (Path.IsPathFullyQualified(normalizedPath))
        {
            candidate = Path.GetFullPath(normalizedPath);
        }
        else if (normalizedPath.StartsWith(Path.DirectorySeparatorChar) ||
            normalizedPath.StartsWith(Path.AltDirectorySeparatorChar))
        {
            string relative = normalizedPath.TrimStart(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string driveCandidate = Path.GetFullPath(Path.Combine(
                Path.GetPathRoot(syncRoot) ?? syncRoot,
                relative));
            candidate = driveCandidate.Equals(syncRoot, StringComparison.OrdinalIgnoreCase) ||
                driveCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                ? driveCandidate
                : Path.GetFullPath(Path.Combine(syncRoot, relative));
        }
        else
        {
            candidate = Path.GetFullPath(Path.Combine(syncRoot, normalizedPath));
        }

        if (!candidate.Equals(syncRoot, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The callback path '{normalizedPath}' is outside the sync root '{syncRoot}'.");
        }

        return candidate;
    }

    internal static void RejectReparsePoints(string path)
    {
        string fullPath = NormalizeFullPath(path, nameof(path));
        string root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException($"The path has no recognizable root: {fullPath}");
        string current = root;

        foreach (string segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                FileAttributes attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException(
                        $"The sample refuses to follow the reparse point '{current}'.");
                }
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
        }
    }

    private static string NormalizeFullPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The path is invalid.", parameterName, exception);
        }
    }

    private static bool IsSameOrChild(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
