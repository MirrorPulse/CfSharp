namespace CfSharp;

/// <summary>Identifies the high-level operation represented by a provider callback.</summary>
public enum CloudProviderRequestKind
{
    /// <summary>Windows requests a byte range for a file.</summary>
    FetchData = 0,

    /// <summary>Windows requests child placeholders for a directory.</summary>
    FetchPlaceholders = 1,

    /// <summary>Windows asks the provider to validate hydrated bytes.</summary>
    ValidateData = 2,

    /// <summary>Windows asks whether a placeholder may be dehydrated.</summary>
    Dehydrate = 3,

    /// <summary>Windows asks whether a placeholder may be deleted.</summary>
    Delete = 4,

    /// <summary>Windows asks whether a placeholder may be renamed or moved.</summary>
    Rename = 5,

    /// <summary>Windows reports completion of a namespace or content notification.</summary>
    CompletionNotification = 6,
}

/// <summary>Identifies a validation outcome returned by an application provider.</summary>
public enum CloudProviderValidationStatus
{
    /// <summary>The requested bytes match provider state and may be acknowledged.</summary>
    Accepted = 0,

    /// <summary>The bytes do not match provider state and must not be marked in sync.</summary>
    Rejected = 1,

    /// <summary>The provider has newer metadata or identity and requires a restart decision.</summary>
    Changed = 2,
}

/// <summary>Identifies an application decision for a guarded Cloud Files operation.</summary>
public enum CloudProviderPolicyDecision
{
    /// <summary>Allow the requested operation.</summary>
    Allow = 0,

    /// <summary>Reject the requested operation with a stable Cloud Files failure.</summary>
    Deny = 1,
}

/// <summary>Identifies a completion notification delivered to an application provider.</summary>
public enum CloudProviderNotificationKind
{
    /// <summary>A placeholder open completed.</summary>
    FileOpenCompleted = 0,

    /// <summary>A placeholder close completed.</summary>
    FileCloseCompleted = 1,

    /// <summary>A placeholder dehydration completed.</summary>
    DehydrateCompleted = 2,

    /// <summary>A placeholder deletion completed.</summary>
    DeleteCompleted = 3,

    /// <summary>A placeholder rename or move completed.</summary>
    RenameCompleted = 4,
}

/// <summary>Contains an immutable directory placeholder request copied from Windows callback memory.</summary>
public sealed class CloudProviderFetchPlaceholdersRequest
{
    private readonly byte[] _directoryIdentity;

    internal CloudProviderFetchPlaceholdersRequest(
        string normalizedPath,
        ReadOnlySpan<byte> directoryIdentity,
        string searchPattern,
        string? continuationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);
        ArgumentNullException.ThrowIfNull(searchPattern);
        NormalizedPath = normalizedPath;
        _directoryIdentity = directoryIdentity.ToArray();
        SearchPattern = searchPattern;
        ContinuationToken = continuationToken;
    }

    /// <summary>Gets the normalized callback path of the directory being populated.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined directory identity.</summary>
    public ReadOnlyMemory<byte> DirectoryIdentity => _directoryIdentity;

    /// <summary>Gets the Windows search pattern, or an empty pattern when none was supplied.</summary>
    public string SearchPattern { get; }

    /// <summary>Gets the last continuation token committed for this directory, when any.</summary>
    public string? ContinuationToken { get; }
}

/// <summary>Contains an immutable page of child placeholder specifications.</summary>
public sealed class CloudProviderDirectoryPage
{
    private readonly IReadOnlyList<CloudPlaceholderSpec> _children;

    /// <summary>Initializes a validated provider page.</summary>
    /// <param name="children">Ordered child specifications. The collection is copied.</param>
    /// <param name="continuationToken">Opaque token for the next page, or null when complete.</param>
    /// <param name="totalCount">Known total child count, or null when unknown.</param>
    public CloudProviderDirectoryPage(
        IEnumerable<CloudPlaceholderSpec> children,
        string? continuationToken = null,
        long? totalCount = null)
    {
        ArgumentNullException.ThrowIfNull(children);
        CloudPlaceholderSpec[] copy = children.ToArray();
        if (copy.Any(static child => child is null))
        {
            throw new ArgumentException("A directory page cannot contain a null child.", nameof(children));
        }

        if (continuationToken is not null && string.IsNullOrWhiteSpace(continuationToken))
        {
            throw new ArgumentException("A continuation token must contain text.", nameof(continuationToken));
        }

        if (totalCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be negative.");
        }

        _children = Array.AsReadOnly(copy);
        ContinuationToken = continuationToken;
        TotalCount = totalCount;
    }

    /// <summary>Gets the ordered, immutable child list.</summary>
    public IReadOnlyList<CloudPlaceholderSpec> Children => _children;

    /// <summary>Gets the opaque token for the next page, or null when this is the final page.</summary>
    public string? ContinuationToken { get; }

    /// <summary>Gets a known total child count, or null when the provider cannot know it.</summary>
    public long? TotalCount { get; }

    /// <summary>Gets whether this page completes the directory enumeration.</summary>
    public bool IsComplete => ContinuationToken is null;
}

/// <summary>Contains an immutable data-validation request copied from Windows callback memory.</summary>
public sealed class CloudProviderValidateDataRequest
{
    private readonly byte[] _fileIdentity;

    internal CloudProviderValidateDataRequest(
        string normalizedPath,
        ReadOnlySpan<byte> fileIdentity,
        long fileSize,
        long offset,
        long length,
        bool explicitHydration)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        if (offset > fileSize || length > fileSize - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The validation range exceeds the logical file size.");
        }

        NormalizedPath = normalizedPath;
        _fileIdentity = fileIdentity.ToArray();
        FileSize = fileSize;
        Range = new CloudFileRange(offset, length);
        IsExplicitHydration = explicitHydration;
    }

    /// <summary>Gets the normalized callback path.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined identity.</summary>
    public ReadOnlyMemory<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets the logical file size reported by Windows.</summary>
    public long FileSize { get; }

    /// <summary>Gets the validated byte range.</summary>
    public CloudFileRange Range { get; }

    /// <summary>Gets whether Windows requested validation after explicit hydration.</summary>
    public bool IsExplicitHydration { get; }
}

/// <summary>Contains an immutable request to approve dehydration.</summary>
public sealed class CloudProviderDehydrateRequest
{
    private readonly byte[] _fileIdentity;

    internal CloudProviderDehydrateRequest(
        string normalizedPath,
        ReadOnlySpan<byte> fileIdentity,
        bool isBackground,
        CloudProviderDehydrationReason reason)
    {
        NormalizedPath = normalizedPath;
        _fileIdentity = fileIdentity.ToArray();
        IsBackground = isBackground;
        Reason = reason;
    }

    /// <summary>Gets the normalized callback path.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined identity.</summary>
    public ReadOnlyMemory<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets whether Windows requested background dehydration.</summary>
    public bool IsBackground { get; }

    /// <summary>Gets the operating-system dehydration reason.</summary>
    public CloudProviderDehydrationReason Reason { get; }
}

/// <summary>Identifies why Windows requested dehydration.</summary>
public enum CloudProviderDehydrationReason
{
    /// <summary>No reason was supplied.</summary>
    None = 0,

    /// <summary>A user requested dehydration.</summary>
    UserManual = 1,

    /// <summary>Windows is under local-storage pressure.</summary>
    SystemLowSpace = 2,

    /// <summary>Windows is reclaiming inactive content.</summary>
    SystemInactivity = 3,

    /// <summary>Windows is performing an operating-system upgrade.</summary>
    SystemOsUpgrade = 4,
}

/// <summary>Contains an immutable request to approve deletion.</summary>
public sealed class CloudProviderDeleteRequest
{
    private readonly byte[] _fileIdentity;

    internal CloudProviderDeleteRequest(string normalizedPath, ReadOnlySpan<byte> fileIdentity, bool isDirectory, bool isUndelete)
    {
        NormalizedPath = normalizedPath;
        _fileIdentity = fileIdentity.ToArray();
        IsDirectory = isDirectory;
        IsUndelete = isUndelete;
    }

    /// <summary>Gets the normalized callback path.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined identity.</summary>
    public ReadOnlyMemory<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets whether the target is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets whether the notification restores a previously deleted placeholder.</summary>
    public bool IsUndelete { get; }
}

/// <summary>Contains an immutable request to approve a rename or move.</summary>
public sealed class CloudProviderRenameRequest
{
    private readonly byte[] _fileIdentity;

    internal CloudProviderRenameRequest(
        string normalizedPath,
        ReadOnlySpan<byte> fileIdentity,
        string targetPath,
        bool isDirectory,
        bool sourceInScope,
        bool targetInScope)
    {
        NormalizedPath = normalizedPath;
        _fileIdentity = fileIdentity.ToArray();
        TargetPath = targetPath;
        IsDirectory = isDirectory;
        SourceInScope = sourceInScope;
        TargetInScope = targetInScope;
    }

    /// <summary>Gets the normalized source path.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined identity.</summary>
    public ReadOnlyMemory<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets the normalized target path supplied by Windows.</summary>
    public string TargetPath { get; }

    /// <summary>Gets whether the source is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets whether the source is inside the connected sync root.</summary>
    public bool SourceInScope { get; }

    /// <summary>Gets whether the target is inside the connected sync root.</summary>
    public bool TargetInScope { get; }
}

/// <summary>Contains an immutable provider validation result.</summary>
public sealed class CloudProviderValidationResult
{
    private CloudProviderValidationResult(CloudProviderValidationStatus status)
    {
        Status = status;
    }

    /// <summary>Gets the validation status.</summary>
    public CloudProviderValidationStatus Status { get; }

    /// <summary>Creates an accepted validation result.</summary>
    public static CloudProviderValidationResult Accepted() => new(CloudProviderValidationStatus.Accepted);

    /// <summary>Creates a rejected validation result.</summary>
    public static CloudProviderValidationResult Rejected() => new(CloudProviderValidationStatus.Rejected);

    /// <summary>Creates a changed-state validation result.</summary>
    public static CloudProviderValidationResult Changed() => new(CloudProviderValidationStatus.Changed);
}

/// <summary>Contains an immutable completion notification copied from Windows callback memory.</summary>
public sealed class CloudProviderCompletionNotification
{
    private readonly byte[] _fileIdentity;

    internal CloudProviderCompletionNotification(
        CloudProviderNotificationKind kind,
        string normalizedPath,
        ReadOnlySpan<byte> fileIdentity,
        uint flags,
        string? relatedPath)
    {
        Kind = kind;
        NormalizedPath = normalizedPath;
        _fileIdentity = fileIdentity.ToArray();
        Flags = flags;
        RelatedPath = relatedPath;
    }

    /// <summary>Gets the notification kind.</summary>
    public CloudProviderNotificationKind Kind { get; }

    /// <summary>Gets the normalized target path.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets an owned copy of the provider-defined identity.</summary>
    public ReadOnlyMemory<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets native notification flags represented as an opaque bit mask.</summary>
    public uint Flags { get; }

    /// <summary>Gets the related source path for a rename completion, when supplied.</summary>
    public string? RelatedPath { get; }
}
