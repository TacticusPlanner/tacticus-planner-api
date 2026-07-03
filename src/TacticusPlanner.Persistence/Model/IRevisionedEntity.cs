namespace TacticusPlanner.Persistence.Model;

public interface IRevisionedEntity
{
    long Revision { get; set; }
}
