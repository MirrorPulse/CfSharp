namespace CfSharp;

public abstract partial class CloudItem
{
    /// <summary>Opens a safe lifetime lease over this item.</summary>
    /// <param name="options">Access and oplock options, or read-only defaults.</param>
    /// <param name="cancellationToken">Token checked before admitting the operation.</param>
    /// <returns>A lease that exposes no raw handle and owns the open lifetime.</returns>
    /// <exception cref="ArgumentException">The option combination is invalid.</exception>
    /// <exception cref="InvalidOperationException">No connected provider can own a transfer lease.</exception>
    /// <exception cref="ObjectDisposedException">The owning file system is stopping or disposed.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the protected open.</exception>
    public ValueTask<CloudItemLease> AcquireLeaseAsync(
        CloudItemLeaseOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.AcquireLeaseAsync(this, options ?? CloudItemLeaseOptions.ReadOnly, cancellationToken);
}
