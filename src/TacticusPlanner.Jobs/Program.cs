using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TacticusPlanner.Jobs;
using TacticusPlanner.ServiceDefaults;

// One process per Container Apps Job execution: start the host (telemetry exporters), run the job named by the
// first argument, stop the host (which flushes telemetry), and exit with the job's outcome.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddPlannerDatabase(builder.Configuration);
builder.Services.AddBatchJobs();

using var host = builder.Build();
await host.StartAsync();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TacticusPlanner.Jobs");
var exitCode = await JobRunner.RunAsync(host.Services, args, logger, lifetime.ApplicationStopping);
await host.StopAsync();
return exitCode;
