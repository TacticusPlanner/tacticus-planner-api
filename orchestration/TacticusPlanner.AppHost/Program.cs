using System.Globalization;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPersistentLifetime()
    .WithDataVolume("tacticus-planner-postgres-data");

var plannerDatabase = postgres.AddDatabase("planner-db", "tacticus_planner");

var api = builder
    .AddProject<Projects.TacticusPlanner_Api>("api")
    .WithReference(plannerDatabase)
    .WaitFor(plannerDatabase)
    .WithHttpHealthCheck("/health/ready");

var clientAppPath = builder.Configuration["ClientAppPath"]
    ?? "../../../tacticus-planner-apps/apps/web";
var clientWorkspacePath = Path.GetFullPath(
    Path.Combine(clientAppPath, "..", ".."),
    builder.AppHostDirectory
);
var webPort = builder.Configuration["WebPort"] is { } configuredWebPort
    ? int.Parse(configuredWebPort, CultureInfo.InvariantCulture)
    : 5173;

var web = builder
    .AddJavaScriptApp("web", clientWorkspacePath)
    .WithPnpm()
    .WithRunScript("dev:web")
    .WithHttpEndpoint(port: webPort, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("http"));

api.WithEnvironment("Cors__AllowedOrigins__0", web.GetEndpoint("http"));

builder.Build().Run();
