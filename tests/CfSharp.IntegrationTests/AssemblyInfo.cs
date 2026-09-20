using Xunit;

// Cloud Files integration tests mutate persistent, machine-wide sync-root state. Running
// registration and cleanup concurrently can make CldApi observe transiently inconsistent state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
