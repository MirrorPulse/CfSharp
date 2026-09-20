using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;

namespace CfSharp;

/// <summary>
/// Owns the process-scoped resources for one persistent Windows Cloud Files sync root.
/// </summary>
/// <remarks>
/// <para>
/// Build an instance with <see cref="CreateBuilder(string)"/>, then call <see cref="StartAsync"/> once
/// before performing file-system operations. Starting validates the local root again, opens the
/// configured durable state store, opens or registers the persistent sync root, and optionally
/// connects a content provider. The state-store factory is never called for invalid local-root or
/// builder configuration.
/// </para>
/// <para>
/// A successfully opened state store and provider session are owned exclusively by this instance.
/// Synchronous or asynchronous disposal first stops the provider session and then disposes the
/// store. Disposal never unregisters the persistent sync root; account removal remains an explicit
/// <see cref="CloudSyncRoot.Unregister"/> operation.
/// </para>
/// <para>
/// Public lifecycle members are safe for concurrent calls. Startup and disposal are serialized.
/// A failed startup can be retried only when all partially acquired resources were released. Item
/// objects returned by later APIs are immutable and do not inherit this object's synchronization
/// primitive. If resource disposal fails, successfully released resources remain released and the
/// instance stays in <see cref="CloudFileSystemLifecycleState.Stopping"/> so disposal can be
/// retried for the remaining resources.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudFileSystem : IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ICloudStateStoreFactory _stateStoreFactory;
    private readonly SyncRootRegistrationOptions? _registration;
    private readonly ICloudFileContentProvider? _contentProvider;
    private readonly ICloudFileSystemRuntime _runtime;
    private ICloudStateStore? _stateStore;
    private ICloudFileSystemRuntimeSession? _runtimeSession;
    private int _state = (int)CloudFileSystemLifecycleState.Created;

    private CloudFileSystem(
        string syncRootPath,
        ICloudStateStoreFactory stateStoreFactory,
        SyncRootRegistrationOptions? registration,
        ICloudFileContentProvider? contentProvider,
        ICloudFileSystemRuntime runtime)
    {
        SyncRootPath = syncRootPath;
        _stateStoreFactory = stateStoreFactory;
        _registration = registration;
        _contentProvider = contentProvider;
        _runtime = runtime;
    }

    /// <summary>Gets the normalized absolute path of the managed sync root.</summary>
    public string SyncRootPath { get; }

    /// <summary>Gets a thread-safe snapshot of the current process lifecycle state.</summary>
    public CloudFileSystemLifecycleState LifecycleState =>
        (CloudFileSystemLifecycleState)Volatile.Read(ref _state);

    /// <summary>Creates a mutable builder for one local sync-root directory.</summary>
    /// <param name="syncRootPath">
    /// Absolute path of the existing directory that is or will become a Cloud Files sync root.
    /// </param>
    /// <returns>A builder that requires an explicit durable state-store factory.</returns>
    /// <exception cref="ArgumentException">
    /// The path is empty, relative, or cannot be normalized.
    /// </exception>
    public static Builder CreateBuilder(string syncRootPath) =>
        new(syncRootPath, WindowsCloudFileSystemRuntime.Instance);

    internal static Builder CreateBuilder(
        string syncRootPath,
        ICloudFileSystemRuntime runtime) =>
        new(syncRootPath, runtime);

    /// <summary>Opens all configured process resources and makes the file system ready.</summary>
    /// <param name="cancellationToken">
    /// Token that cancels waiting for lifecycle ownership or opening the state store. Native
    /// registration and connection calls are synchronous and cannot be interrupted once entered.
    /// </param>
    /// <returns>An operation that completes after the store and optional provider session open.</returns>
    /// <exception cref="DirectoryNotFoundException">
    /// The configured local root no longer exists when startup begins.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The instance is already starting or started.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The instance is stopping or disposed.</exception>
    /// <exception cref="OperationCanceledException">Startup was canceled before completion.</exception>
    /// <exception cref="CloudFilesException">
    /// Windows rejects sync-root registration, lookup, or provider connection.
    /// </exception>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloudFileSystemLifecycleState state = LifecycleState;
            ObjectDisposedException.ThrowIf(
                state is CloudFileSystemLifecycleState.Stopping or CloudFileSystemLifecycleState.Disposed,
                this);

            if (state is not CloudFileSystemLifecycleState.Created)
            {
                throw new InvalidOperationException("The cloud file system has already been started.");
            }

            if (!Directory.Exists(SyncRootPath))
            {
                throw new DirectoryNotFoundException(
                    $"The sync-root directory does not exist: '{SyncRootPath}'.");
            }

            Volatile.Write(ref _state, (int)CloudFileSystemLifecycleState.Starting);
            ICloudStateStore? openedStore = null;
            ICloudFileSystemRuntimeSession? openedRuntimeSession = null;
            try
            {
                openedStore = await _stateStoreFactory
                    .OpenAsync(new CloudStateStoreContext(SyncRootPath), cancellationToken)
                    .ConfigureAwait(false);
                if (openedStore is null)
                {
                    throw new InvalidOperationException(
                        "The state-store factory returned no store.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                openedRuntimeSession = _runtime.Start(
                    SyncRootPath,
                    _registration,
                    _contentProvider);
                if (openedRuntimeSession is null)
                {
                    throw new InvalidOperationException(
                        "The Cloud Files runtime returned no session.");
                }

                _stateStore = openedStore;
                _runtimeSession = openedRuntimeSession;
                Volatile.Write(ref _state, (int)CloudFileSystemLifecycleState.Started);
            }
            catch (Exception startFailure)
            {
                _runtimeSession = openedRuntimeSession;
                _stateStore = openedStore;
                IReadOnlyList<Exception> cleanupFailures = await DisposeOwnedResourcesAsync()
                    .ConfigureAwait(false);
                Volatile.Write(
                    ref _state,
                    cleanupFailures.Count == 0
                        ? (int)CloudFileSystemLifecycleState.Created
                        : (int)CloudFileSystemLifecycleState.Stopping);
                if (cleanupFailures.Count == 0)
                {
                    ExceptionDispatchInfo.Throw(startFailure);
                }

                throw new AggregateException(
                    "Cloud file-system startup failed and acquired resources could not be released cleanly.",
                    new[] { startFailure }.Concat(cleanupFailures));
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Synchronously releases process resources without unregistering the sync root.</summary>
    /// <exception cref="AggregateException">
    /// More than one owned resource failed during disposal. Every resource is still attempted.
    /// Failed resources remain owned so disposal can be retried.
    /// </exception>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases process resources without unregistering the persistent sync root.</summary>
    /// <returns>An operation that completes after the provider session and state store terminate.</returns>
    /// <exception cref="AggregateException">
    /// More than one owned resource failed during disposal. Every resource is still attempted.
    /// Failed resources remain owned so disposal can be retried.
    /// </exception>
    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (LifecycleState is CloudFileSystemLifecycleState.Disposed)
            {
                return;
            }

            Volatile.Write(ref _state, (int)CloudFileSystemLifecycleState.Stopping);
            IReadOnlyList<Exception> failures = await DisposeOwnedResourcesAsync()
                .ConfigureAwait(false);
            if (failures.Count == 0)
            {
                Volatile.Write(ref _state, (int)CloudFileSystemLifecycleState.Disposed);
                GC.SuppressFinalize(this);
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Throw(failures[0]);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "Cloud file-system resources could not be released cleanly.",
                    failures);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask<IReadOnlyList<Exception>> DisposeOwnedResourcesAsync()
    {
        List<Exception>? failures = null;
        if (_runtimeSession is not null)
        {
            try
            {
                await _runtimeSession.DisposeAsync().ConfigureAwait(false);
                _runtimeSession = null;
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (_stateStore is not null)
        {
            try
            {
                await _stateStore.DisposeAsync().ConfigureAwait(false);
                _stateStore = null;
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        return failures ?? [];
    }

    /// <summary>Builds immutable lifecycle configuration for one cloud file system.</summary>
    /// <remarks>
    /// A builder is mutable and not thread-safe. <see cref="Build"/> may be called repeatedly;
    /// each result has independent process ownership but uses the same configured dependencies.
    /// A state-store implementation may reject concurrently started instances.
    /// </remarks>
    public sealed class Builder
    {
        private readonly string _syncRootPath;
        private readonly ICloudFileSystemRuntime _runtime;
        private ICloudStateStoreFactory? _stateStoreFactory;
        private SyncRootRegistrationOptions? _registration;
        private ICloudFileContentProvider? _contentProvider;

        internal Builder(string syncRootPath, ICloudFileSystemRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            _syncRootPath = NormalizePath(syncRootPath);
            _runtime = runtime;
        }

        /// <summary>Sets the required factory for durable synchronization coordination state.</summary>
        /// <param name="stateStoreFactory">
        /// Factory opened during <see cref="StartAsync"/> after local validation succeeds.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithStateStore(ICloudStateStoreFactory stateStoreFactory)
        {
            ArgumentNullException.ThrowIfNull(stateStoreFactory);
            _stateStoreFactory = stateStoreFactory;
            return this;
        }

        /// <summary>Configures persistent registration to apply during startup.</summary>
        /// <param name="registration">
        /// Immutable registration and policy values. Without this option, startup requires the
        /// local path to be registered already.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithRegistration(SyncRootRegistrationOptions registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            _registration = registration;
            return this;
        }

        /// <summary>Configures the optional provider used for Windows hydration requests.</summary>
        /// <param name="contentProvider">
        /// Thread-safe provider whose callback lifetime is owned by the started file system.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithContentProvider(ICloudFileContentProvider contentProvider)
        {
            ArgumentNullException.ThrowIfNull(contentProvider);
            _contentProvider = contentProvider;
            return this;
        }

        /// <summary>Validates configuration and creates an inactive file-system facade.</summary>
        /// <returns>
        /// A resource-free instance in the <see cref="CloudFileSystemLifecycleState.Created"/>
        /// state. Call <see cref="StartAsync"/> to acquire process resources.
        /// </returns>
        /// <exception cref="DirectoryNotFoundException">The local root does not exist.</exception>
        /// <exception cref="InvalidOperationException">No state-store factory was configured.</exception>
        public CloudFileSystem Build()
        {
            if (!Directory.Exists(_syncRootPath))
            {
                throw new DirectoryNotFoundException(
                    $"The sync-root directory does not exist: '{_syncRootPath}'.");
            }

            if (_stateStoreFactory is null)
            {
                throw new InvalidOperationException(
                    "A cloud state-store factory must be configured before building.");
            }

            return new CloudFileSystem(
                _syncRootPath,
                _stateStoreFactory,
                _registration,
                _contentProvider,
                _runtime);
        }

        private static string NormalizePath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException(
                    "The sync-root path must be fully qualified.",
                    nameof(path));
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
    }
}
