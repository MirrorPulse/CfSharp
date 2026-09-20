using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Associates a callback type with an unmanaged callback entry point.
/// </summary>
/// <remarks>
/// An array passed to <see cref="CfApi.CfConnectSyncRoot"/> must end with an entry whose
/// <see cref="Type"/> is <see cref="CfCallbackType.None"/> and whose
/// <see cref="Callback"/> is null. The array and every callback entry point must remain valid
/// until <see cref="CfApi.CfDisconnectSyncRoot"/> returns.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfCallbackRegistration
{
    /// <summary>
    /// Gets the terminator entry required at the end of a callback-registration array.
    /// Mirrors <c>CF_CALLBACK_REGISTRATION_END</c>.
    /// </summary>
    public static CfCallbackRegistration End => new()
    {
        Type = CfCallbackType.None,
        Callback = null,
    };

    /// <summary>Identifies the callback request handled by this entry.</summary>
    public CfCallbackType Type;

    /// <summary>
    /// Unmanaged stdcall entry point. The callback must not allow exceptions to cross the native
    /// boundary and must treat both arguments as borrowed memory valid only for that invocation.
    /// </summary>
    public delegate* unmanaged[Stdcall]<CfCallbackInfo*, CfCallbackParameters*, void> Callback;
}
