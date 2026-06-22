var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPersistentLifetime()
    .WithDataVolume("tacticus-planner-postgres-data");

var plannerDatabase = postgres.AddDatabase("planner-db", "tacticus_planner");

builder
    .AddProject<Projects.TacticusPlanner_Api>("api")
    .WithReference(plannerDatabase)
    .WaitFor(plannerDatabase);

builder.Build().Run();
