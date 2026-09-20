using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Describes the version and capability level of the Windows Cloud Files platform.
/// </summary>
/// <remarks>
/// <para>
/// This structure is the managed representation of the native <c>CF_PLATFORM_INFO</c>
/// structure. It is blittable and owns no native memory, so values may be copied and
/// used independently after the native call returns.
/// </para>
/// <para>
/// The structure is available on Windows 10, version 1709 and later. Individual
/// capability decisions should use <see cref="IntegrationNumber"/> rather than infer
/// behavior only from the Windows build number.
/// </para>
/// <para>
/// Instances are not synchronized. Concurrent callers should use separate values or
/// provide their own synchronization while mutating the public native fields.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfPlatformInfo
{
    /// <summary>
    /// The build number of the installed Cloud Files platform.
    /// </summary>
    /// <remarks>
    /// Windows servicing may change this value without changing the available API
    /// contract.
    /// </remarks>
    public uint BuildNumber;

    /// <summary>
    /// The revision number of the installed Cloud Files platform.
    /// </summary>
    /// <remarks>
    /// Windows servicing may change this value without changing the available API
    /// contract.
    /// </remarks>
    public uint RevisionNumber;

    /// <summary>
    /// The monotonically increasing Cloud Files platform capability level.
    /// </summary>
    /// <remarks>
    /// Microsoft documents this value as the authoritative indicator for API contracts
    /// and the availability of critical Cloud Files platform fixes.
    /// </remarks>
    public uint IntegrationNumber;
}
