namespace TacticusPlanner.Domain.Common;

public interface IRevisionedEntity
{
    long Revision { get; set; }
}
