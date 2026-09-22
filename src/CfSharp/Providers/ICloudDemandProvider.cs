namespace CfSharp;

/// <summary>Extends file hydration with the demand operations used by the Phase 6 runtime.</summary>
/// <remarks>
/// The interface keeps the existing file-stream contract and adds optional default operations.
/// A provider may implement only the callback families it supports; unsupported operations are
/// rejected by the session with a deterministic Cloud Files failure. All methods may be called
/// concurrently and must honor the supplied cancellation token.
/// </remarks>
public interface ICloudDemandProvider : ICloudFileContentProvider
{
    /// <summary>Supplies one ordered page of child placeholders for a directory request.</summary>
    ValueTask<CloudProviderDirectoryPage> FetchChildrenAsync(
        CloudProviderFetchPlaceholdersRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CloudProviderDirectoryPage>(
            new NotSupportedException("This provider does not support directory population."));

    /// <summary>Validates a range of hydrated file data.</summary>
    ValueTask<CloudProviderValidationResult> ValidateDataAsync(
        CloudProviderValidateDataRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<CloudProviderValidationResult>(
            new NotSupportedException("This provider does not support data validation."));

    /// <summary>Approves or rejects a dehydration request.</summary>
    ValueTask<CloudProviderPolicyDecision> ApproveDehydrateAsync(
        CloudProviderDehydrateRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(CloudProviderPolicyDecision.Deny);

    /// <summary>Approves or rejects a delete request.</summary>
    ValueTask<CloudProviderPolicyDecision> ApproveDeleteAsync(
        CloudProviderDeleteRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(CloudProviderPolicyDecision.Deny);

    /// <summary>Approves or rejects a rename or move request.</summary>
    ValueTask<CloudProviderPolicyDecision> ApproveRenameAsync(
        CloudProviderRenameRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(CloudProviderPolicyDecision.Deny);

    /// <summary>Receives an asynchronous completion notification.</summary>
    ValueTask OnCompletionAsync(
        CloudProviderCompletionNotification notification,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
