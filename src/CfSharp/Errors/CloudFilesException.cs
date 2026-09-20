using System.Runtime.InteropServices;

namespace CfSharp;

/// <summary>
/// Represents an operating-system failure reported while performing a Cloud Files operation.
/// </summary>
/// <remarks>
/// <para>
/// The inherited <see cref="Exception.HResult"/> property preserves the original native
/// <c>HRESULT</c>. When that value was created from a Win32 error, <see cref="Win32ErrorCode"/>
/// exposes the embedded Win32 code without discarding the original value.
/// </para>
/// <para>
/// This exception owns no native resources. Its CfSharp-defined properties are immutable and
/// may be read concurrently after construction. Mutable members inherited from
/// <see cref="Exception"/>, such as <see cref="Exception.Data"/>, are not synchronized.
/// </para>
/// </remarks>
public sealed class CloudFilesException : Exception
{
    private const int FacilityWin32Mask = unchecked((int)0xFFFF0000);
    private const int HResultFromWin32Prefix = unchecked((int)0x80070000);

    /// <summary>
    /// Initializes an exception without an operation name or custom message.
    /// </summary>
    public CloudFilesException()
    {
    }

    /// <summary>
    /// Initializes an exception with a caller-provided message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public CloudFilesException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes an exception with a caller-provided message and underlying exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public CloudFilesException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    private CloudFilesException(string operation, int hresult, string message, Exception? innerException)
        : base(message, innerException)
    {
        Operation = operation;
        HResult = hresult;
    }

    /// <summary>
    /// Gets the stable CfSharp operation name associated with the failure, when available.
    /// </summary>
    /// <value>
    /// An operation name suitable for diagnostics, or <see langword="null"/> when an exception
    /// was created directly by application code.
    /// </value>
    public string? Operation { get; }

    /// <summary>
    /// Gets the Win32 error code embedded in the native <c>HRESULT</c>, when applicable.
    /// </summary>
    /// <value>
    /// The low 16-bit Win32 error code for an <c>HRESULT_FROM_WIN32</c> value; otherwise,
    /// <see langword="null"/>.
    /// </value>
    public int? Win32ErrorCode =>
        (HResult & FacilityWin32Mask) == HResultFromWin32Prefix ? HResult & 0xFFFF : null;

    internal static CloudFilesException FromHResult(string operation, int hresult)
    {
        Exception? nativeException = Marshal.GetExceptionForHR(hresult);
        string nativeMessage = nativeException?.Message ?? "The operating system reported an unknown failure.";
        string message =
            $"Cloud Files operation '{operation}' failed with HRESULT 0x{unchecked((uint)hresult):X8}: " +
            nativeMessage;

        return new CloudFilesException(operation, hresult, message, nativeException);
    }
}
