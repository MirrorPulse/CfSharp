using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CfSharp;

/// <summary>
/// Identifies one CfSharp-managed placeholder independently of its mutable local path.
/// </summary>
/// <remarks>
/// <para>
/// The encoded value contains a format marker, version, stable item identifier, UTF-8 remote
/// identifier, and optional UTF-8 revision. It is suitable for the native Cloud Files identity
/// blob and never exceeds the platform's 4 KiB limit. Encoding is deterministic and independent
/// of the current process architecture.
/// </para>
/// <para>
/// Windows stores placeholder identity data with the file-system item and returns it to provider
/// callbacks. Do not place credentials, access tokens, or other secrets in either string.
/// Instances are immutable and safe for concurrent reads.
/// </para>
/// </remarks>
public sealed class CloudPlaceholderIdentity : IEquatable<CloudPlaceholderIdentity>
{
    private const int HeaderLength = 32;
    private const byte CurrentVersion = 1;
    private const byte RevisionPresent = 0x01;
    private static readonly byte[] Magic = "CFSH"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly int _remoteIdByteCount;
    private readonly int _remoteRevisionByteCount;

    /// <summary>Initializes a stable placeholder identity.</summary>
    /// <param name="itemId">Non-empty CfSharp item identifier.</param>
    /// <param name="remoteId">Non-empty provider-defined stable object identifier.</param>
    /// <param name="remoteRevision">Optional provider-defined revision.</param>
    /// <exception cref="ArgumentException">
    /// An identifier is empty, contains invalid UTF-16, or produces an identity larger than the
    /// native Cloud Files limit.
    /// </exception>
    public CloudPlaceholderIdentity(Guid itemId, string remoteId, string? remoteRevision = null)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("The item identifier cannot be empty.", nameof(itemId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId);
        _remoteIdByteCount = GetUtf8ByteCount(remoteId, nameof(remoteId));
        _remoteRevisionByteCount = remoteRevision is null
            ? 0
            : GetUtf8ByteCount(remoteRevision, nameof(remoteRevision));

        int encodedLength;
        try
        {
            encodedLength = checked(HeaderLength + _remoteIdByteCount + _remoteRevisionByteCount);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("The placeholder identity is too large.", nameof(remoteId), exception);
        }

        if (encodedLength > SyncRootRegistrationOptions.MaxFileIdentityLength)
        {
            throw new ArgumentException(
                $"The encoded placeholder identity cannot exceed " +
                $"{SyncRootRegistrationOptions.MaxFileIdentityLength} bytes.",
                nameof(remoteId));
        }

        ItemId = itemId;
        RemoteId = remoteId;
        RemoteRevision = remoteRevision;
        EncodedLength = encodedLength;
    }

    /// <summary>Gets the stable CfSharp item identifier.</summary>
    public Guid ItemId { get; }

    /// <summary>Gets the provider-defined stable remote object identifier.</summary>
    public string RemoteId { get; }

    /// <summary>Gets the optional provider-defined remote revision.</summary>
    public string? RemoteRevision { get; }

    /// <summary>Gets the exact number of bytes produced by <see cref="Encode"/>.</summary>
    public int EncodedLength { get; }

    /// <summary>Creates an identity with a new random CfSharp item identifier.</summary>
    /// <param name="remoteId">Non-empty provider-defined stable object identifier.</param>
    /// <param name="remoteRevision">Optional provider-defined revision.</param>
    /// <returns>An immutable identity whose <see cref="ItemId"/> is newly generated.</returns>
    public static CloudPlaceholderIdentity Create(string remoteId, string? remoteRevision = null) =>
        new(Guid.NewGuid(), remoteId, remoteRevision);

    /// <summary>Encodes this value into the documented CfSharp native identity envelope.</summary>
    /// <returns>A newly allocated owned byte array.</returns>
    public byte[] Encode()
    {
        byte[] destination = new byte[EncodedLength];
        Magic.CopyTo(destination, 0);
        destination[4] = CurrentVersion;
        destination[5] = RemoteRevision is null ? (byte)0 : RevisionPresent;
        ItemId.TryWriteBytes(destination.AsSpan(8, 16), bigEndian: true, out int bytesWritten);
        if (bytesWritten != 16)
        {
            throw new InvalidOperationException("The item identifier could not be encoded.");
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(24, 4), (uint)_remoteIdByteCount);
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.AsSpan(28, 4),
            (uint)_remoteRevisionByteCount);
        int offset = HeaderLength;
        offset += StrictUtf8.GetBytes(RemoteId, destination.AsSpan(offset, _remoteIdByteCount));
        if (RemoteRevision is not null)
        {
            StrictUtf8.GetBytes(
                RemoteRevision,
                destination.AsSpan(offset, _remoteRevisionByteCount));
        }

        return destination;
    }

    /// <summary>Decodes a CfSharp native placeholder-identity envelope.</summary>
    /// <param name="encoded">Complete encoded identity bytes.</param>
    /// <returns>The immutable decoded identity.</returns>
    /// <exception cref="InvalidDataException">The envelope is malformed.</exception>
    /// <exception cref="NotSupportedException">The envelope uses an unknown format version.</exception>
    public static CloudPlaceholderIdentity Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < HeaderLength || !encoded[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The value is not a CfSharp placeholder identity.");
        }

        if (encoded[4] != CurrentVersion)
        {
            throw new NotSupportedException(
                $"CfSharp placeholder identity version {encoded[4]} is not supported.");
        }

        byte flags = encoded[5];
        if ((flags & ~RevisionPresent) != 0 || encoded[6] != 0 || encoded[7] != 0)
        {
            throw new InvalidDataException("The placeholder identity contains unsupported flags.");
        }

        Guid itemId = new(encoded.Slice(8, 16), bigEndian: true);
        uint remoteIdLength = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(24, 4));
        uint revisionLength = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(28, 4));
        if ((flags & RevisionPresent) == 0 && revisionLength != 0)
        {
            throw new InvalidDataException("A revision length was supplied without the revision flag.");
        }

        int payloadLength;
        try
        {
            payloadLength = checked((int)remoteIdLength + (int)revisionLength);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The placeholder identity lengths are invalid.", exception);
        }

        if (encoded.Length != HeaderLength + payloadLength)
        {
            throw new InvalidDataException("The placeholder identity length does not match its header.");
        }

        try
        {
            string remoteId = StrictUtf8.GetString(encoded.Slice(HeaderLength, (int)remoteIdLength));
            string? revision = (flags & RevisionPresent) == 0
                ? null
                : StrictUtf8.GetString(encoded.Slice(
                    HeaderLength + (int)remoteIdLength,
                    (int)revisionLength));
            return new CloudPlaceholderIdentity(itemId, remoteId, revision);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The placeholder identity contains invalid UTF-8.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The placeholder identity contains invalid values.", exception);
        }
    }

    /// <summary>Attempts to decode a CfSharp native placeholder-identity envelope.</summary>
    /// <param name="encoded">Complete encoded identity bytes.</param>
    /// <param name="identity">Receives the decoded identity on success.</param>
    /// <returns><see langword="true"/> when the value is valid and supported.</returns>
    public static bool TryDecode(
        ReadOnlySpan<byte> encoded,
        [NotNullWhen(true)] out CloudPlaceholderIdentity? identity)
    {
        try
        {
            identity = Decode(encoded);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException)
        {
            identity = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Equals(CloudPlaceholderIdentity? other) =>
        other is not null &&
        ItemId == other.ItemId &&
        string.Equals(RemoteId, other.RemoteId, StringComparison.Ordinal) &&
        string.Equals(RemoteRevision, other.RemoteRevision, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CloudPlaceholderIdentity);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ItemId, RemoteId, RemoteRevision);

    /// <inheritdoc/>
    public override string ToString() => ItemId.ToString("D");

    private static int GetUtf8ByteCount(string value, string parameterName)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Placeholder identity strings must contain valid Unicode.",
                parameterName,
                exception);
        }
    }
}
