using System.Collections.Concurrent;
using TacticusPlanner.Api.Features.AccountManagement;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Stands in for Microsoft Graph: records which object ids a purge asked to delete.</summary>
internal sealed class RecordingExternalIdentityDeleter : IExternalIdentityDeleter
{
    private readonly ConcurrentBag<string?> requestedObjectIds = [];

    public Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken)
    {
        requestedObjectIds.Add(objectId);
        return Task.FromResult(true);
    }

    public bool WasRequested(string objectId) => requestedObjectIds.Contains(objectId);
}
