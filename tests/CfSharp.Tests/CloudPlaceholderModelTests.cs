using System.Buffers.Binary;
using System.Runtime.Versioning;

namespace CfSharp.Tests;

public sealed class CloudPlaceholderModelTests
{
    [Fact]
    public void IdentityCodecRoundTripsDeterministicallyAndOwnsEncodedBuffers()
    {
        Guid itemId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        CloudPlaceholderIdentity identity = new(itemId, "remote/报告", "revision-7");

        byte[] first = identity.Encode();
        byte[] second = identity.Encode();
        CloudPlaceholderIdentity decoded = CloudPlaceholderIdentity.Decode(first);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal("CFSH"u8.ToArray(), first[..4]);
        Assert.Equal(1, first[4]);
        Assert.Equal(32 + "remote/报告"u8.Length + "revision-7"u8.Length, identity.EncodedLength);
        Assert.Equal((uint)"remote/报告"u8.Length, BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(24, 4)));
        Assert.Equal(identity, decoded);
        Assert.True(CloudPlaceholderIdentity.TryDecode(first, out CloudPlaceholderIdentity? attempted));
        Assert.Equal(identity, attempted);

        first[0] = 0;
        Assert.Equal((byte)'C', identity.Encode()[0]);
    }

    [Fact]
    public void IdentityCodecRejectsInvalidAndUnsupportedValues()
    {
        Assert.Throws<ArgumentException>(() => new CloudPlaceholderIdentity(Guid.Empty, "remote"));
        Assert.Throws<ArgumentException>(() => new CloudPlaceholderIdentity(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => new CloudPlaceholderIdentity(Guid.NewGuid(), "\ud800"));
        ArgumentException invalidRemote = Assert.Throws<ArgumentException>(() =>
            new CloudPlaceholderIdentity(Guid.NewGuid(), "\ud800", "revision"));
        Assert.Equal("remoteId", invalidRemote.ParamName);
        ArgumentException invalidRevision = Assert.Throws<ArgumentException>(() =>
            new CloudPlaceholderIdentity(Guid.NewGuid(), "remote", "\ud800"));
        Assert.Equal("remoteRevision", invalidRevision.ParamName);
        Assert.Throws<ArgumentException>(() =>
            new CloudPlaceholderIdentity(Guid.NewGuid(), new string('x', 4096)));

        byte[] encoded = new CloudPlaceholderIdentity(Guid.NewGuid(), "remote").Encode();
        encoded[4] = 2;
        Assert.Throws<NotSupportedException>(() => CloudPlaceholderIdentity.Decode(encoded));
        Assert.False(CloudPlaceholderIdentity.TryDecode(encoded, out _));

        encoded = new CloudPlaceholderIdentity(Guid.NewGuid(), "remote").Encode();
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(24, 4), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => CloudPlaceholderIdentity.Decode(encoded));
        Assert.False(CloudPlaceholderIdentity.TryDecode("opaque"u8, out _));
    }

    [Fact]
    public void MetadataBuildersNormalizeTimeAndEnforceItemKind()
    {
        DateTimeOffset localTime = new(2026, 9, 21, 12, 30, 0, TimeSpan.FromHours(8));
        CloudPlaceholderMetadata file = CloudPlaceholderMetadata.CreateFileBuilder()
            .WithAttributes(FileAttributes.ReadOnly)
            .WithCreationTime(localTime)
            .WithLastWriteTime(localTime)
            .Build();
        CloudPlaceholderMetadata directory = CloudPlaceholderMetadata.CreateDirectoryBuilder().Build();

        Assert.Equal(CloudItemKind.File, file.Kind);
        Assert.Equal(FileAttributes.ReadOnly, file.Attributes);
        Assert.Equal(localTime.ToUniversalTime(), file.CreationTime);
        Assert.Equal(CloudItemKind.Directory, directory.Kind);
        Assert.True(directory.Attributes.HasFlag(FileAttributes.Directory));
        Assert.Throws<ArgumentException>(() => CloudPlaceholderMetadata
            .CreateFileBuilder()
            .WithAttributes(FileAttributes.Directory)
            .Build());
        Assert.Throws<ArgumentException>(() => CloudPlaceholderMetadata
            .CreateDirectoryBuilder()
            .WithAttributes(FileAttributes.Normal)
            .Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => CloudPlaceholderMetadata
            .CreateFileBuilder()
            .WithCreationTime(DateTimeOffset.MinValue)
            .Build());
    }

    [Fact]
    public void PlaceholderSpecsUseSafeDefaultsAndStableBuilderIdentity()
    {
        CloudFilePlaceholderSpec.Builder fileBuilder =
            CloudFilePlaceholderSpec.CreateBuilder("report.pdf", "remote-report", 8192);
        CloudFilePlaceholderSpec first = fileBuilder.Build();
        CloudFilePlaceholderSpec second = fileBuilder.Build();
        CloudDirectoryPlaceholderSpec directory = CloudDirectoryPlaceholderSpec
            .CreateBuilder("Archive", "remote-archive")
            .Build();

        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(CloudItemKind.File, first.Kind);
        Assert.Equal(8192, first.Length);
        Assert.Equal(CloudAvailabilityTarget.OnlineOnly, first.InitialAvailability);
        Assert.True(first.InitiallyInSync);
        Assert.Equal(CloudPlaceholderCollisionBehavior.Fail, first.CollisionBehavior);
        Assert.Equal(CloudDirectoryPopulationState.Partial, directory.PopulationState);
        Assert.Throws<ArgumentException>(() => CloudFilePlaceholderSpec
            .CreateBuilder(Path.Combine("nested", "report.pdf"), "remote", 1)
            .Build());
        Assert.Throws<ArgumentException>(() => CloudFilePlaceholderSpec
            .CreateBuilder("report.pdf ", "remote", 1)
            .Build());
        Assert.Throws<ArgumentException>(() => CloudFilePlaceholderSpec
            .CreateBuilder("CON.txt", "remote", 1)
            .Build());
        Assert.Throws<ArgumentException>(() => CloudFilePlaceholderSpec
            .CreateBuilder("COM¹", "remote", 1)
            .Build());
        Assert.Throws<ArgumentException>(() => CloudFilePlaceholderSpec
            .CreateBuilder(new string('x', 256), "remote", 1)
            .Build());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CloudFilePlaceholderSpec.CreateBuilder("report.pdf", "remote", -1));
        Assert.Throws<ArgumentException>(() => fileBuilder.WithMetadata(
            CloudPlaceholderMetadata.CreateDirectoryBuilder().Build()));
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void RichStatusEncodingRejectsUnpairedUtf16Surrogates()
    {
        Assert.Throws<ArgumentException>(() => CloudSyncRoot.EncodeDescription("bad\ud800"));

        byte[] encoded = CloudSyncRoot.EncodeDescription("status");
        Assert.Equal((byte)'s', encoded[0]);
        Assert.Equal(0, encoded[^1]);
        Assert.Equal(0, encoded[^2]);
    }

    [Fact]
    public void FileRangesValidateFiniteAndToEndForms()
    {
        CloudFileRange finite = new(10, 20);
        CloudFileRange toEnd = CloudFileRange.ToEnd(10);

        Assert.Equal(10, finite.Offset);
        Assert.Equal(20, finite.Length);
        Assert.False(finite.ExtendsToEnd);
        Assert.Equal(10, toEnd.Offset);
        Assert.True(toEnd.ExtendsToEnd);
        Assert.Equal(CloudFileRange.ToEnd(0), CloudFileRange.WholeFile);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFileRange(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFileRange(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudFileRange(long.MaxValue, 1));
        Assert.Throws<ArgumentException>(() => default(CloudFileRange).Validate("range"));
    }

    [Fact]
    public void ConversionAndStateOptionsRejectContradictions()
    {
        CloudPlaceholderConversionOptions options = CloudPlaceholderConversionOptions
            .CreateBuilder()
            .WithInSyncState()
            .WithPopulationState(CloudDirectoryPopulationState.Complete)
            .WithForceConversion()
            .Build();

        Assert.True(options.MarkInSync);
        Assert.Equal(CloudDirectoryPopulationState.Complete, options.PopulationState);
        Assert.True(options.ForceConversion);
        Assert.Throws<InvalidOperationException>(() => CloudPlaceholderConversionOptions
            .CreateBuilder()
            .WithContentMode(CloudFileContentMode.AlwaysFull)
            .WithDehydration()
            .Build());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudInSyncChangeOptions(expectedUsn: 0));
        Assert.Null(new CloudInSyncChangeOptions().ExpectedUsn);
        Assert.False(CloudMoveOptions.Default.ReplaceExisting);
        Assert.True(CloudRecursiveOperationOptions.Default.IncludeRoot);
        Assert.False(CloudRecursiveOperationOptions.Default.StopOnFirstFailure);
    }

    [Fact]
    public void PlaceholderPatchUsesExplicitChangesAndCopiesRanges()
    {
        List<CloudFileRange> ranges = [new CloudFileRange(0, 4096)];
        CloudPlaceholderPatch patch = CloudPlaceholderPatch.CreateBuilder()
            .WithIdentity(new CloudPlaceholderIdentity(Guid.NewGuid(), "remote"))
            .WithInSyncState(inSync: false)
            .WithContentMode(CloudFileContentMode.AllowPartial)
            .WithDehydratedRanges(ranges)
            .WithExtrinsicPropertyRemoval()
            .WithInSyncVerification()
            .WithMetadata(CloudPlaceholderMetadata.CreateFileBuilder().Build())
            .WithFileSize(8192)
            .WithExpectedUsn(42)
            .Build();
        ranges.Add(new CloudFileRange(4096, 4096));

        Assert.Equal(CloudPlaceholderIdentityChange.Replace, patch.IdentityChange);
        Assert.Equal(CloudPlaceholderSynchronizationChange.MarkNotInSync, patch.SynchronizationChange);
        Assert.Single(patch.DehydrateRanges);
        Assert.Equal(42, patch.ExpectedUsn);
        Assert.Equal(8192, patch.FileSize);
        Assert.True(patch.RemoveExtrinsicProperties);
        Assert.True(patch.RequireInSync);
        Assert.Throws<InvalidOperationException>(() => CloudPlaceholderPatch.CreateBuilder().Build());
        Assert.Throws<InvalidOperationException>(() => CloudPlaceholderPatch
            .CreateBuilder()
            .WithFullDehydration()
            .WithDehydratedRanges([new CloudFileRange(0, 1)])
            .Build());
        Assert.Throws<InvalidOperationException>(() => CloudPlaceholderPatch
            .CreateBuilder()
            .WithContentMode(CloudFileContentMode.AlwaysFull)
            .WithFullDehydration()
            .Build());
        Assert.Throws<InvalidOperationException>(() => CloudPlaceholderPatch
            .CreateBuilder()
            .WithFileSize(1)
            .Build());
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void BatchAndRecursiveResultsCopyInputAndPreserveFailures()
    {
        CloudFilePlaceholderSpec specification = CloudFilePlaceholderSpec
            .CreateBuilder("report.pdf", "remote", 1)
            .Build();
        CloudFilesException failure = CloudFilesException.FromHResult(
            "CloudDirectory.CreatePlaceholders",
            @"C:\sync\report.pdf",
            unchecked((int)0x80070020));
        List<CloudPlaceholderBatchEntryResult> batchEntries =
        [
            new(
                specification,
                @"C:\sync\report.pdf",
                CloudItemOperationStatus.Failed,
                CloudPlaceholderCreationProgress.None,
                createUsn: null,
                item: null,
                failure),
        ];
        CloudPlaceholderBatchResult batch = new(batchEntries);
        batchEntries.Clear();

        Assert.Single(batch.Entries);
        Assert.Equal(0, batch.SucceededCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.False(batch.IsSuccessful);
        Assert.Throws<AggregateException>(batch.ThrowIfAnyFailed);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CloudPlaceholderBatchEntryResult>)batch.Entries).Add(batch.Entries[0]));

        CloudPlaceholderBatchEntryResult matched = new(
            specification,
            @"C:\sync\report.pdf",
            CloudItemOperationStatus.Succeeded,
            CloudPlaceholderCreationProgress.ExistingPlaceholderMatched |
                CloudPlaceholderCreationProgress.DurableStatePersisted,
            createUsn: null,
            new CloudFile(null!, @"C:\sync\report.pdf", "report.pdf"),
            error: null);
        Assert.True(matched.DurableStatePersisted);
        Assert.Null(matched.CreateUsn);

        List<CloudRecursiveOperationEntryResult> recursiveEntries =
        [
            new(
                @"C:\sync\report.pdf",
                CloudItemKind.File,
                CloudItemOperationStatus.Succeeded,
                error: null),
        ];
        CloudRecursiveOperationResult recursive = new(recursiveEntries);
        recursiveEntries.Clear();
        Assert.Single(recursive.Entries);
        Assert.True(recursive.IsSuccessful);
        recursive.ThrowIfAnyFailed();
    }
}
