using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Persistence.Interceptors;

public sealed class EntityMetadataInterceptor(TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        UpdateEntities(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        UpdateEntities(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        TouchOwnersOfChangedOwnedEntities(context);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity is BaseEntity entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = now;
                }

                entity.UpdatedAt = now;
            }

            if (entry.Entity is IRevisionedEntity revisionedEntity)
            {
                revisionedEntity.Revision = entry.State == EntityState.Added
                    ? 1
                    : revisionedEntity.Revision + 1;
            }
        }
    }

    /// <summary>
    /// A change to an owned entity (a JSON column such as a plan's entries or a goal's target) leaves its owner
    /// row Unchanged, so the loop above would skip the owner's <c>UpdatedAt</c> and revision bump. Marks the
    /// root owner of every added, modified or deleted owned entity as modified, so a jsonb-only change is a real
    /// revision without each writer touching the row by hand.
    /// </summary>
    private static void TouchOwnersOfChangedOwnedEntities(DbContext context)
    {
        var entries = context.ChangeTracker.Entries().ToArray();
        var changedOwned = entries
            .Where(entry => entry.Metadata.IsOwned()
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        foreach (var owned in changedOwned)
        {
            if (FindRootOwner(entries, owned) is not { State: EntityState.Unchanged } owner)
            {
                continue;
            }

            if (owner.Entity is BaseEntity)
            {
                owner.Property(nameof(BaseEntity.UpdatedAt)).IsModified = true;
            }
            else if (owner.Entity is IRevisionedEntity)
            {
                owner.Property(nameof(IRevisionedEntity.Revision)).IsModified = true;
            }
        }
    }

    /// <summary>Follows the ownership chain up to the first non-owned entry, matching each owner by key.</summary>
    private static EntityEntry? FindRootOwner(IReadOnlyList<EntityEntry> entries, EntityEntry owned)
    {
        var current = owned;
        while (current.Metadata.IsOwned())
        {
            var ownership = current.Metadata.FindOwnership();
            if (ownership is null)
            {
                return null;
            }

            var keyValues = ownership.Properties
                .Select(property => current.Property(property.Name).CurrentValue
                    ?? current.Property(property.Name).OriginalValue)
                .ToArray();
            var principalKey = ownership.PrincipalKey.Properties;
            var owner = entries
                .FirstOrDefault(entry => entry.Metadata == ownership.PrincipalEntityType
                    && principalKey.Select((property, index) =>
                            Equals(entry.Property(property.Name).CurrentValue, keyValues[index]))
                        .All(match => match));
            if (owner is null)
            {
                return null;
            }

            current = owner;
        }

        return current;
    }
}
