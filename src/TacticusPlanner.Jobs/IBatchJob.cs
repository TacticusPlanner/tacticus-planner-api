namespace TacticusPlanner.Jobs;

/// <summary>
/// One batch job the jobs container can run: <c>dotnet TacticusPlanner.Jobs.dll &lt;name&gt;</c>, where the name is the
/// key the job is registered under (see <see cref="JobRegistration"/>). A job runs to completion and the process
/// exits; a throw fails the Container Apps Job execution. Jobs must be safe to run again after a failure.
/// </summary>
public interface IBatchJob
{
    Task RunAsync(CancellationToken cancellationToken);
}
