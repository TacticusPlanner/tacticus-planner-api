namespace TacticusPlanner.Domain.Common;

public interface IGuidValueObject
{
    Guid Value { get; }
}

public abstract class BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public abstract class BaseEntity<TId> : BaseEntity
{
    public TId Id { get; set; } = default!;
}
