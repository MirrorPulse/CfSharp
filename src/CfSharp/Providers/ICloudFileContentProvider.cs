namespace CfSharp;

/// <summary>Supplies remote file content when Windows requests placeholder hydration.</summary>
/// <remarks>
/// <para>
/// Implementations may be called concurrently and must therefore synchronize mutable state.
/// The returned stream must be readable and represent the complete logical file. If seekable,
/// CfSharp positions it at the requested offset; a non-seekable stream is accepted only for a
/// request beginning at offset zero.
/// </para>
/// <para>
/// The stream is disposed by CfSharp after the request completes. Observe the cancellation token
/// promptly; it is signaled when Windows cancels the request or the provider session shuts down.
/// </para>
/// </remarks>
public interface ICloudFileContentProvider
{
    /// <summary>Opens the content source for a Windows hydration request.</summary>
    /// <param name="request">Copied request metadata with no native-memory lifetime.</param>
    /// <param name="cancellationToken">Signals request cancellation or session shutdown.</param>
    /// <returns>A readable stream whose ownership transfers to CfSharp.</returns>
    ValueTask<Stream> OpenReadAsync(
        CloudFileFetchRequest request,
        CancellationToken cancellationToken);
}
