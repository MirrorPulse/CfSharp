namespace CfSharp.Tests;

public sealed class CloudRemoteChangeModelTests
{
    [Fact]
    public void ChangeCanonicalizesPathsAndCopiesCursor()
    {
        byte[] cursor = [1, 2, 3];
        CloudPlaceholderMetadata metadata = CloudPlaceholderMetadata.CreateFileBuilder().Build();
        CloudRemoteChange change = new(
            "change-1",
            CloudRemoteChangeKind.FileUpsert,
            "remote-1",
            "revision-1",
            CloudItemKind.File,
            "Documents/report.txt",
            length: 12,
            metadata: metadata,
            cursorAfter: cursor);

        cursor[0] = 9;

        Assert.Equal("Documents" + Path.DirectorySeparatorChar + "report.txt", change.RelativePath);
        Assert.Equal(new byte[] { 1, 2, 3 }, change.CursorAfter.ToArray());
        Assert.Equal(12, change.Length);
    }

    [Fact]
    public void ChangeRejectsInvalidShapes()
    {
        CloudPlaceholderMetadata fileMetadata = CloudPlaceholderMetadata.CreateFileBuilder().Build();
        Assert.Throws<ArgumentException>(() => new CloudRemoteChange(
            "change",
            CloudRemoteChangeKind.FileUpsert,
            "remote",
            "revision",
            CloudItemKind.File,
            "report.txt",
            metadata: fileMetadata));

        Assert.Throws<ArgumentException>(() => new CloudRemoteChange(
            "change",
            CloudRemoteChangeKind.Move,
            "remote",
            "revision",
            CloudItemKind.File,
            "new.txt"));

        Assert.Throws<ArgumentException>(() => new CloudRemoteChange(
            "change",
            CloudRemoteChangeKind.MetadataUpdate,
            "remote",
            "revision",
            CloudItemKind.File,
            "../outside.txt",
            metadata: fileMetadata));
    }

    [Fact]
    public void BatchFingerprintIsDeterministicAndCoversOpaqueValues()
    {
        CloudRemoteChange first = CreateFileChange("change-1", new byte[] { 1, 2 });
        CloudRemoteChange second = CreateFileChange("change-2", new byte[] { 3, 4 });
        CloudRemoteChangeBatch left = new(
            "batch",
            new byte[] { 7 },
            [first, second],
            new byte[] { 9 });
        CloudRemoteChangeBatch right = new(
            "batch",
            new byte[] { 7 },
            [
                CreateFileChange("change-1", new byte[] { 1, 2 }),
                CreateFileChange("change-2", new byte[] { 3, 4 }),
            ],
            new byte[] { 9 });
        CloudRemoteChangeBatch changed = new(
            "batch",
            new byte[] { 7 },
            [
                CreateFileChange("change-1", new byte[] { 1, 2 }),
                CreateFileChange("change-2", new byte[] { 3, 5 }),
            ],
            new byte[] { 9 });

        Assert.Equal(left.Fingerprint.ToArray(), right.Fingerprint.ToArray());
        Assert.False(left.Fingerprint.Span.SequenceEqual(changed.Fingerprint.Span));
    }

    [Fact]
    public void BatchRejectsDuplicateChangeIds()
    {
        CloudRemoteChange first = CreateFileChange("duplicate", Array.Empty<byte>());
        CloudRemoteChange second = CreateFileChange("duplicate", Array.Empty<byte>());

        Assert.Throws<ArgumentException>(() => new CloudRemoteChangeBatch(
            "batch",
            default,
            [first, second],
            default));
    }

    [Fact]
    public void ConflictResolutionRequiresPathOnlyForKeepBoth()
    {
        CloudRemoteConflictResolution keepBoth = new(
            CloudRemoteConflictDecision.KeepBoth,
            "Conflicts" + Path.DirectorySeparatorChar + "report.txt");

        Assert.Equal("Conflicts" + Path.DirectorySeparatorChar + "report.txt", keepBoth.KeepBothRelativePath);
        Assert.Throws<ArgumentException>(() => new CloudRemoteConflictResolution(
            CloudRemoteConflictDecision.Defer,
            "report.txt"));
        Assert.Throws<ArgumentException>(() => new CloudRemoteConflictResolution(
            CloudRemoteConflictDecision.KeepBoth,
            "../report.txt"));
    }

    [Fact]
    public void ApplyOptionsValidateBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudRemoteApplyOptions { MaximumEntries = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudRemoteApplyOptions { EchoSuppressionLifetime = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudRemoteApplyOptions { EchoSuppressionObservationCount = 0 }.Validate());
    }

    private static CloudRemoteChange CreateFileChange(string changeId, ReadOnlyMemory<byte> cursor) =>
        new(
            changeId,
            CloudRemoteChangeKind.FileUpsert,
            "remote-" + changeId,
            "revision-" + changeId,
            CloudItemKind.File,
            changeId + ".txt",
            length: 1,
            metadata: CloudPlaceholderMetadata.CreateFileBuilder().Build(),
            cursorAfter: cursor);
}
