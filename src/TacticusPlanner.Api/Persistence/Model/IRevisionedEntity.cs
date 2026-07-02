namespace TacticusPlanner.Api.Persistence.Model;

public interface IRevisionedEntity
{
    long Revision { get; set; }
}
