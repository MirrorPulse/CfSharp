namespace CfSharp;

internal readonly record struct CloudItemOperationScope
{
    private CloudItemOperationScope(string path, bool includesDescendants)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An operation scope path must be fully qualified.", nameof(path));
        }

        Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        IncludesDescendants = includesDescendants;
    }

    internal string Path { get; }

    internal bool IncludesDescendants { get; }

    internal static CloudItemOperationScope Exact(string path) => new(path, includesDescendants: false);

    internal static CloudItemOperationScope Subtree(string path) => new(path, includesDescendants: true);

    internal bool ConflictsWith(CloudItemOperationScope other)
    {
        if (string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (IncludesDescendants && IsSameOrDescendant(other.Path, Path)) ||
            (other.IncludesDescendants && IsSameOrDescendant(Path, other.Path));
    }

    private static bool IsSameOrDescendant(string candidate, string ancestor)
    {
        if (string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (System.IO.Path.EndsInDirectorySeparator(ancestor))
        {
            return true;
        }

        return candidate.Length > ancestor.Length &&
            IsDirectorySeparator(candidate[ancestor.Length]);
    }

    private static bool IsDirectorySeparator(char value) =>
        value == System.IO.Path.DirectorySeparatorChar ||
        value == System.IO.Path.AltDirectorySeparatorChar;
}

internal sealed class CloudItemOperationCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<long, IReadOnlyList<CloudItemOperationScope>> _active = [];
    private readonly LinkedList<Waiter> _waiters = [];
    private long _nextLeaseId;

    internal async ValueTask<CloudItemOperationPathLease> AcquireAsync(
        IEnumerable<CloudItemOperationScope> scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        cancellationToken.ThrowIfCancellationRequested();
        List<CloudItemOperationScope> normalized = NormalizeScopes(scopes);
        Waiter waiter;

        lock (_gate)
        {
            if (!ConflictsWithActive(normalized) && !ConflictsWithPending(normalized))
            {
                return Grant(normalized);
            }

            waiter = new Waiter(normalized);
            waiter.Node = _waiters.AddLast(waiter);
        }

        try
        {
            return await waiter.Completion.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!Cancel(waiter))
            {
                CloudItemOperationPathLease granted = await waiter.Completion.Task.ConfigureAwait(false);
                granted.Dispose();
            }

            throw;
        }
    }

    private static List<CloudItemOperationScope> NormalizeScopes(
        IEnumerable<CloudItemOperationScope> scopes)
    {
        List<CloudItemOperationScope> normalized = [];
        foreach (CloudItemOperationScope scope in scopes)
        {
            bool redundant = false;
            for (int index = normalized.Count - 1; index >= 0; index--)
            {
                CloudItemOperationScope existing = normalized[index];
                if (string.Equals(existing.Path, scope.Path, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.IncludesDescendants || !scope.IncludesDescendants)
                    {
                        redundant = true;
                        break;
                    }

                    normalized.RemoveAt(index);
                    continue;
                }

                if (existing.IncludesDescendants && IsContainedBy(scope.Path, existing.Path))
                {
                    redundant = true;
                    break;
                }

                if (scope.IncludesDescendants && IsContainedBy(existing.Path, scope.Path))
                {
                    normalized.RemoveAt(index);
                }
            }

            if (!redundant)
            {
                normalized.Add(scope);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one operation scope is required.", nameof(scopes));
        }

        normalized.Sort(static (left, right) =>
        {
            int pathComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return pathComparison != 0
                ? pathComparison
                : right.IncludesDescendants.CompareTo(left.IncludesDescendants);
        });
        return normalized;
    }

    private static bool IsContainedBy(string candidate, string ancestor) =>
        CloudItemOperationScope.Subtree(ancestor)
            .ConflictsWith(CloudItemOperationScope.Exact(candidate));

    private bool Cancel(Waiter waiter)
    {
        lock (_gate)
        {
            if (waiter.Node is null)
            {
                return false;
            }

            _waiters.Remove(waiter.Node);
            waiter.Node = null;
            ProcessWaiters();
            return true;
        }
    }

    private CloudItemOperationPathLease Grant(IReadOnlyList<CloudItemOperationScope> scopes)
    {
        long leaseId = ++_nextLeaseId;
        _active.Add(leaseId, scopes);
        return new CloudItemOperationPathLease(this, leaseId);
    }

    private void Release(long leaseId)
    {
        lock (_gate)
        {
            if (!_active.Remove(leaseId))
            {
                return;
            }

            ProcessWaiters();
        }
    }

    private void ProcessWaiters()
    {
        LinkedListNode<Waiter>? node = _waiters.First;
        while (node is not null)
        {
            LinkedListNode<Waiter>? next = node.Next;
            Waiter waiter = node.Value;
            if (!ConflictsWithActive(waiter.Scopes) && !ConflictsWithEarlierPending(node))
            {
                _waiters.Remove(node);
                waiter.Node = null;
                waiter.Completion.SetResult(Grant(waiter.Scopes));
            }

            node = next;
        }
    }

    private bool ConflictsWithActive(IReadOnlyList<CloudItemOperationScope> requested)
    {
        foreach (IReadOnlyList<CloudItemOperationScope> activeScopes in _active.Values)
        {
            if (ScopesConflict(requested, activeScopes))
            {
                return true;
            }
        }

        return false;
    }

    private bool ConflictsWithPending(IReadOnlyList<CloudItemOperationScope> requested)
    {
        foreach (Waiter waiter in _waiters)
        {
            if (ScopesConflict(requested, waiter.Scopes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConflictsWithEarlierPending(LinkedListNode<Waiter> node)
    {
        for (LinkedListNode<Waiter>? earlier = node.Previous;
             earlier is not null;
             earlier = earlier.Previous)
        {
            if (ScopesConflict(node.Value.Scopes, earlier.Value.Scopes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ScopesConflict(
        IReadOnlyList<CloudItemOperationScope> left,
        IReadOnlyList<CloudItemOperationScope> right)
    {
        foreach (CloudItemOperationScope leftScope in left)
        {
            foreach (CloudItemOperationScope rightScope in right)
            {
                if (leftScope.ConflictsWith(rightScope))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal sealed class CloudItemOperationPathLease : IDisposable
    {
        private CloudItemOperationCoordinator? _owner;
        private readonly long _leaseId;

        internal CloudItemOperationPathLease(CloudItemOperationCoordinator owner, long leaseId)
        {
            _owner = owner;
            _leaseId = leaseId;
        }

        public void Dispose()
        {
            CloudItemOperationCoordinator? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_leaseId);
        }
    }

    private sealed class Waiter
    {
        internal Waiter(IReadOnlyList<CloudItemOperationScope> scopes)
        {
            Scopes = scopes;
        }

        internal IReadOnlyList<CloudItemOperationScope> Scopes { get; }

        internal TaskCompletionSource<CloudItemOperationPathLease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal LinkedListNode<Waiter>? Node { get; set; }
    }
}
