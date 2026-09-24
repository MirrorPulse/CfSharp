using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace CfSharp;

/// <summary>
/// Holds no-delete directory handles for a path walk while a namespace operation is issued.
/// </summary>
/// <remarks>
/// The Cloud Files path APIs accept strings rather than directory handles. Holding each existing
/// ancestor with delete sharing disabled prevents another process from replacing the checked
/// directory chain between validation and the native call. Reparse points are opened without
/// following the final component and are rejected. This is intentionally an internal guard; the
/// native API remains the authority for the final create operation.
/// </remarks>
[SupportedOSPlatform("windows10.0.16299")]
internal sealed class CloudPathHandleLease : IDisposable
{
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FileAttributeTagInformation = 9;
    private const uint FileAttributeReparsePoint = 0x00000400;

    private readonly List<SafeFileHandle> _handles;

    private CloudPathHandleLease(List<SafeFileHandle> handles)
    {
        _handles = handles;
    }

    /// <summary>Opens every existing directory from the volume root through the target parent.</summary>
    internal static CloudPathHandleLease OpenDirectoryChain(
        string syncRootPath,
        string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(syncRootPath));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
        if (!IsSameOrChild(root, target))
        {
            throw new ArgumentException(
                "The target directory must remain beneath the sync root.",
                nameof(targetDirectory));
        }

        List<SafeFileHandle> handles = [];
        try
        {
            string volumeRoot = Path.GetPathRoot(target)!;
            string current = volumeRoot;
            OpenAndAdd(
                current,
                handles,
                isDirectory: true,
                allowReparsePoint: string.Equals(current, root, StringComparison.OrdinalIgnoreCase));
            string remainder = target[volumeRoot.Length..];
            foreach (string segment in remainder.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                OpenAndAdd(
                    current,
                    handles,
                    isDirectory: true,
                    allowReparsePoint: string.Equals(current, root, StringComparison.OrdinalIgnoreCase));
            }

            return new CloudPathHandleLease(handles);
        }
        catch
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    /// <summary>Opens an existing final item without following its reparse point.</summary>
    internal static SafeFileHandle OpenExistingItem(string path, bool isDirectory)
    {
        SafeFileHandle handle = Open(path, isDirectory);
        try
        {
            EnsureNotReparsePoint(handle, path, allowReparsePoint: false);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        for (int index = _handles.Count - 1; index >= 0; index--)
        {
            _handles[index].Dispose();
        }

        _handles.Clear();
    }

    private static void OpenAndAdd(
        string path,
        List<SafeFileHandle> handles,
        bool isDirectory,
        bool allowReparsePoint)
    {
        SafeFileHandle handle = Open(path, isDirectory);
        try
        {
            EnsureNotReparsePoint(handle, path, allowReparsePoint);
            handles.Add(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle Open(string path, bool isDirectory)
    {
        SafeFileHandle handle = CreateFileW(
            path,
            FileReadAttributes,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | (isDirectory ? FileFlagBackupSemantics : 0),
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException(
                $"The path component '{path}' could not be opened without delete sharing.",
                new Win32Exception(error));
        }

        return handle;
    }

    private static unsafe void EnsureNotReparsePoint(
        SafeFileHandle handle,
        string path,
        bool allowReparsePoint)
    {
        FileAttributeTagInfo info = default;
        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInformation,
                &info,
                (uint)sizeof(FileAttributeTagInfo)))
        {
            throw new IOException(
                $"The attributes of path component '{path}' could not be read.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if ((info.FileAttributes & FileAttributeReparsePoint) == 0 && info.ReparseTag == 0)
        {
            return;
        }

        if (allowReparsePoint)
        {
            return;
        }

        if ((info.FileAttributes & FileAttributeReparsePoint) != 0 || info.ReparseTag != 0)
        {
            throw new ArgumentException(
                $"The path component '{path}' cannot be a reparse point.",
                nameof(path));
        }
    }

    private static bool IsSameOrChild(string parentPath, string candidatePath)
    {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        return string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe extern bool GetFileInformationByHandleEx(
        SafeFileHandle fileHandle,
        int fileInformationClass,
        void* fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }
}
