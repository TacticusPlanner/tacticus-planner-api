using System.Collections.Concurrent;
using TacticusPlanner.Api.Features.AccountManagement;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>Stands in for Microsoft Graph: records which object ids a purge asked to delete.</summary>
internal sealed class RecordingExternalIdentityDeleter : IExternalIdentityDeleter
{
    private readonly ConcurrentBag<string?> requestedObjectIds = [];
    private readonly ConcurrentDictionary<string, bool> refusedObjectIds = new();

    public Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken)
    {
        requestedObjectIds.Add(objectId);
        return Task.FromResult(objectId is null || !refusedObjectIds.ContainsKey(objectId));
    }

    /// <summary>Makes deleting <paramref name="objectId"/> report failure, as Graph does when it cannot.</summary>
    public void Refuse(string objectId) => refusedObjectIds[objectId] = true;

    /// <summary>Lets deleting <paramref name="objectId"/> succeed again, as Graph does once it recovers.</summary>
    public void Allow(string objectId) => refusedObjectIds.TryRemove(objectId, out _);

    public int RequestCount(string objectId) => requestedObjectIds.Count(id => id == objectId);

    public bool WasRequested(string objectId) => requestedObjectIds.Contains(objectId);
}
