using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CfSharp.Native;

/// <summary>
/// Exposes direct native entry points from the Windows Cloud Files API.
/// </summary>
/// <remarks>
/// <para>
/// Members of this class preserve native signatures and error values for callers that
/// require direct CFAPI access. They do not translate <c>HRESULT</c> failures into
/// managed exceptions.
/// </para>
/// <para>
/// Unless a member states otherwise, arguments follow the ownership and lifetime rules
/// documented for the corresponding function in <c>cfapi.h</c>. The entry points are
/// loaded from the copy of <c>CldApi.dll</c> in the Windows system directory.
/// </para>
/// </remarks>
public static partial class CfApi
{
    /// <summary>
    /// Retrieves version and capability information for the installed Cloud Files platform.
    /// </summary>
    /// <param name="platformVersion">
    /// Receives a self-contained platform information value. The caller owns the value and
    /// no cleanup is required, regardless of whether the function succeeds.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This function is available on Windows 10, version 1709 and later. Calling it on a
    /// system where <c>CldApi.dll</c> or the export is unavailable may produce the standard
    /// .NET native-library loading exceptions before an <c>HRESULT</c> can be returned.
    /// </para>
    /// <para>
    /// The function has no retained callback or buffer lifetime and may be invoked
    /// concurrently from multiple threads.
    /// </para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlatformInfo))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    public static partial int CfGetPlatformInfo(out CfPlatformInfo platformVersion);
}
