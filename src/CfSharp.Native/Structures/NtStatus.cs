using System.Globalization;
using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Preserves an unmodified 32-bit Windows <c>NTSTATUS</c> value.</summary>
/// <remarks>
/// Cloud Files completion operations accept <c>STATUS_SUCCESS</c> or a
/// <c>STATUS_CLOUD_FILE_*</c> failure. Windows converts unrelated failure values to
/// <c>STATUS_CLOUD_FILE_UNSUCCESSFUL</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct NtStatus : IEquatable<NtStatus>
{
    /// <summary>Successful completion.</summary>
    public static readonly NtStatus Success = new(0x00000000);

    /// <summary>The provider could not complete the cloud-file operation.</summary>
    public static readonly NtStatus CloudFileUnsuccessful = new(unchecked((int)0xC000CF12));

    /// <summary>The provider could not complete the operation because its network was unavailable.</summary>
    public static readonly NtStatus CloudFileNetworkUnavailable = new(unchecked((int)0xC000CF11));

    /// <summary>The platform aborted the cloud-file request.</summary>
    public static readonly NtStatus CloudFileRequestAborted = new(unchecked((int)0xC000CF16));

    /// <summary>The user canceled the cloud-file request.</summary>
    public static readonly NtStatus CloudFileRequestCanceled = new(unchecked((int)0xC000CF1B));

    /// <summary>Initializes a status from its unmodified native value.</summary>
    /// <param name="value">Raw signed 32-bit <c>NTSTATUS</c> value.</param>
    public NtStatus(int value)
    {
        Value = value;
    }

    /// <summary>Gets the unmodified signed 32-bit status value.</summary>
    public int Value { get; }

    /// <summary>Gets whether the value satisfies the native <c>NT_SUCCESS</c> predicate.</summary>
    public bool IsSuccess => Value >= 0;

    /// <inheritdoc/>
    public bool Equals(NtStatus other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NtStatus other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value;

    /// <inheritdoc/>
    public override string ToString() => $"0x{unchecked((uint)Value).ToString("X8", CultureInfo.InvariantCulture)}";

    /// <summary>Compares two native status values.</summary>
    public static bool operator ==(NtStatus left, NtStatus right) => left.Equals(right);

    /// <summary>Compares two native status values.</summary>
    public static bool operator !=(NtStatus left, NtStatus right) => !left.Equals(right);
}
