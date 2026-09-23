using System.Text;

using CfSharp.Native;

namespace CfSharp;

/// <summary>Represents a validated Windows Cloud Files correlation vector value.</summary>
/// <remarks>
/// The value is telemetry metadata, not a synchronization revision. Only native versions 1 and
/// 2 are accepted. The managed value owns no native memory and can be copied freely.
/// </remarks>
public readonly struct CloudCorrelationVector : IEquatable<CloudCorrelationVector>
{
    private const int Version1MaximumLength = 64;
    private const int Version2MaximumLength = 128;

    /// <summary>Initializes a validated correlation vector.</summary>
    /// <param name="version">Native vector version, currently 1 or 2.</param>
    /// <param name="value">Non-empty printable ASCII vector payload.</param>
    /// <exception cref="ArgumentOutOfRangeException">The version is unsupported.</exception>
    /// <exception cref="ArgumentException">The payload is not a valid native vector string.</exception>
    public CloudCorrelationVector(byte version, string value)
    {
        if (version is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Only vector versions 1 and 2 are supported.");
        }

        ValidateValue(version, value, nameof(value));
        Version = version;
        Value = value;
    }

    /// <summary>Gets the native vector version.</summary>
    public byte Version { get; }

    /// <summary>Gets the validated ASCII payload.</summary>
    public string Value { get; }

    /// <summary>Creates a validated vector.</summary>
    public static CloudCorrelationVector Create(byte version, string value) =>
        new(version, value);

    /// <summary>
    /// Parses either a raw version-1/2 payload or the conventional <c>1;payload</c>/<c>2;payload</c>
    /// textual form.
    /// </summary>
    public static bool TryParse(string? value, out CloudCorrelationVector vector)
    {
        vector = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        byte version = 1;
        string payload = value;
        if (value.Length >= 2 && value[0] is '1' or '2' && value[1] is ';' or ':')
        {
            version = (byte)(value[0] - '0');
            payload = value[2..];
        }

        try
        {
            vector = new CloudCorrelationVector(version, payload);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Returns the conventional version-prefixed textual form.</summary>
    public override string ToString() => $"{Version};{Value}";

    /// <inheritdoc/>
    public bool Equals(CloudCorrelationVector other) =>
        Version == other.Version && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CloudCorrelationVector other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Version, Value);

    /// <summary>Compares two correlation vectors.</summary>
    public static bool operator ==(CloudCorrelationVector left, CloudCorrelationVector right) => left.Equals(right);

    /// <summary>Compares two correlation vectors.</summary>
    public static bool operator !=(CloudCorrelationVector left, CloudCorrelationVector right) => !left.Equals(right);

    internal unsafe CfCorrelationVector ToNative()
    {
        CfCorrelationVector native = new() { Version = Version };
        byte[] bytes = Encoding.ASCII.GetBytes(Value);
        fixed (byte* source = bytes)
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                native.Vector[index] = source[index];
            }

            native.Vector[bytes.Length] = 0;
        }

        return native;
    }

    internal static unsafe CloudCorrelationVector? FromNative(CfCorrelationVector native)
    {
        if (native.Version == 0)
        {
            return null;
        }

        if (native.Version is not (1 or 2))
        {
            throw new InvalidDataException($"Windows returned unsupported correlation vector version {native.Version}.");
        }

        int length = 0;
        while (length < 129 && native.Vector[length] != 0)
        {
            length++;
        }

        if (length == 129)
        {
            throw new InvalidDataException("Windows returned a non-terminated correlation vector.");
        }

        byte[] bytes = new byte[length];
        for (int index = 0; index < length; index++)
        {
            bytes[index] = native.Vector[index];
        }

        return new CloudCorrelationVector(native.Version, Encoding.ASCII.GetString(bytes));
    }

    private static void ValidateValue(byte version, string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException("The correlation vector payload cannot be empty.", parameterName);
        }

        int maximum = version == 1 ? Version1MaximumLength : Version2MaximumLength;
        if (value.Length > maximum)
        {
            throw new ArgumentException($"A version {version} vector cannot exceed {maximum} characters.", parameterName);
        }

        foreach (char character in value)
        {
            if (character is '\0' or > '\x7F' || character < '\x20')
            {
                throw new ArgumentException("Correlation vectors must contain printable non-NUL ASCII characters.", parameterName);
            }
        }
    }
}
