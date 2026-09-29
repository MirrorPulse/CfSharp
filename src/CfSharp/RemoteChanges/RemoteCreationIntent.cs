using System.Security.Cryptography;
using System.Text;

namespace CfSharp;

internal sealed record RemoteCreationIntent(
    string RelativePath, CloudItemKind Kind, CloudPlaceholderIdentity Identity, byte[] Fingerprint, bool Committed)
{
    internal const string Prefix = "cfsharp/remote-creation/v1";
    internal const string ObservationsPrefix = "cfsharp/remote-creation-observations/v1";
    private static ReadOnlySpan<byte> EchoMarker => "cfsharp-create/v1:"u8;

    internal static string Name(CloudRemoteChange change) => Prefix + "/" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(change.RemoteId + "\0" + change.ChangeId)));

    internal static byte[] GetFingerprint(CloudRemoteChange change) =>
        new CloudRemoteChangeBatch("creation", Array.Empty<byte>(), [change], Array.Empty<byte>()).Fingerprint.ToArray();

    internal byte[] Encode()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(1);
        writer.Write(RelativePath);
        writer.Write((int)Kind);
        writer.Write(Committed);
        writer.Write(Fingerprint.Length);
        writer.Write(Fingerprint);
        byte[] identity = Identity.Encode();
        writer.Write(identity.Length);
        writer.Write(identity);
        return stream.ToArray();
    }

    internal static RemoteCreationIntent Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            using BinaryReader reader = new(stream, Encoding.UTF8);
            if (reader.ReadInt32() != 1)
            {
                throw new InvalidDataException("Unsupported remote creation intent version.");
            }

            string path = CloudRemotePathValidation.Canonicalize(reader.ReadString(), "path");
            CloudItemKind kind = CloudStateModelValidation.RequireDefined((CloudItemKind)reader.ReadInt32(), "kind");
            bool committed = reader.ReadBoolean();
            int fingerprintLength = reader.ReadInt32();
            if (fingerprintLength != 32)
            {
                throw new InvalidDataException("Invalid creation fingerprint length.");
            }

            byte[] fingerprint = reader.ReadBytes(fingerprintLength);
            int identityLength = reader.ReadInt32();
            if (identityLength <= 0 || identityLength > 4096 || identityLength != stream.Length - stream.Position)
            {
                throw new InvalidDataException("Invalid creation identity length.");
            }

            return new(path, kind, CloudPlaceholderIdentity.Decode(reader.ReadBytes(identityLength)), fingerprint, committed);
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The remote creation intent is corrupt.", exception);
        }
    }

    internal static byte[] EchoPayload(CloudPlaceholderIdentity identity) =>
        [.. EchoMarker, .. identity.Encode()];

    internal static bool IsCreationEcho(CloudEchoSuppressionState suppression) =>
        suppression.Payload.Span.StartsWith(EchoMarker);

    internal static CloudPlaceholderIdentity EchoIdentity(CloudEchoSuppressionState suppression) =>
        CloudPlaceholderIdentity.Decode(suppression.Payload.Span[EchoMarker.Length..]);
}
