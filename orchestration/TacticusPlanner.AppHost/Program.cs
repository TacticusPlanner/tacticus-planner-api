using System.Globalization;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPort = builder.Configuration["PostgresPort"] is { } configuredPostgresPort
    ? int.Parse(configuredPostgresPort, CultureInfo.InvariantCulture)
    : 51441;
var postgresPassword = builder.AddParameter("postgres-password", "postgres-admin", secret: true);
var postgres = builder
    .AddPostgres("postgres", password: postgresPassword, port: postgresPort)
    .WithPersistentLifetime()
    .WithDataVolume("tacticus-planner-postgres-data");

var plannerDatabase = postgres.AddDatabase("planner-db", "tacticus_planner");

var persistenceProjectPath = Path.GetFullPath(
    Path.Combine(
        builder.AppHostDirectory,
        "..",
        "..",
        "src",
        "TacticusPlanner.Persistence",
        "TacticusPlanner.Persistence.csproj"
    )
);

var api = builder
    .AddProject<Projects.TacticusPlanner_Api>("api")
    .WithReference(plannerDatabase)
    .WaitFor(plannerDatabase)
    .WithHttpHealthCheck("/health/ready");

api.AddEFMigrations(
    "api-migrations",
    "TacticusPlanner.Persistence.PlannerDbContext",
    tool => tool.WithEnvironment("ASPNETCORE_URLS", string.Empty)
)
.WithMigrationsProject(persistenceProjectPath);

var clientAppPath = builder.Configuration["ClientAppPath"]
    ?? "../../../tacticus-planner-apps/apps/web";
var clientAppFullPath = Path.GetFullPath(clientAppPath, builder.AppHostDirectory);
var clientWorkspacePath = Path.GetFullPath(
    Path.Combine(clientAppFullPath, "..", "..")
);
var webPort = builder.Configuration["WebPort"] is { } configuredWebPort
    ? int.Parse(configuredWebPort, CultureInfo.InvariantCulture)
    : 5173;
var apiProjectPath = Path.GetFullPath(
    Path.Combine(
        builder.AppHostDirectory,
        "..",
        "..",
        "src",
        "TacticusPlanner.Api",
        "TacticusPlanner.Api.csproj"
    )
);
var webEnvPath = Path.Combine(clientAppFullPath, ".env.local");
builder.AddLocalSetupHealthChecks(apiProjectPath, webEnvPath);

var web = builder
    .AddJavaScriptApp("web", clientWorkspacePath)
    .WithPnpm()
    .WithRunScript("dev:web")
    .WithHttpEndpoint(port: webPort, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("http"));

api.WithEnvironment("Cors__AllowedOrigins__0", web.GetEndpoint("http"));
api.WithHealthCheck(LocalSetupHealthChecks.ApiHealthCheckName);
web.WithHealthCheck(LocalSetupHealthChecks.WebHealthCheckName);

builder.Build().Run();
