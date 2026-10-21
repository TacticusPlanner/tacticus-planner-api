using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

internal sealed class RequestCommandCounter(IHttpContextAccessor accessor) : DbCommandInterceptor
{
    internal const string Header = "X-Test-Sql-Request";
    private readonly ConcurrentDictionary<string, int> counts = new();

    internal int CommandsFor(HttpResponseMessage response) => counts.GetValueOrDefault(response.Headers.GetValues(Header).Single());

    private void Record()
    {
        if (accessor.HttpContext is { } context)
            counts.AddOrUpdate(context.TraceIdentifier, 1, (_, count) => count + 1);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record();
        return ValueTask.FromResult(result);
    }
}

internal sealed class RequestCorrelationFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            context.Response.Headers[RequestCommandCounter.Header] = context.TraceIdentifier;
            await continuation();
        });
        next(app);
    };
}

/// <summary>Injects failure after PostgreSQL has executed SaveChanges, before the caller can commit.</summary>
internal sealed class PersistenceFailureInterceptor(PlannerApiFactory factory) : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is PlannerDbContext db && factory.FailAfterSave?.Invoke(db) == true)
        {
            factory.FailAfterSave = null;
            factory.FailureInjected = true;
            throw new DbUpdateException("Injected failure after persistence and before transaction commit.");
        }
        return ValueTask.FromResult(result);
    }
}
