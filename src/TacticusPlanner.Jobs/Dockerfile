FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TacticusPlanner.Jobs/TacticusPlanner.Jobs.csproj src/TacticusPlanner.Jobs/packages.lock.json src/TacticusPlanner.Jobs/
COPY src/TacticusPlanner.Domain/TacticusPlanner.Domain.csproj src/TacticusPlanner.Domain/packages.lock.json src/TacticusPlanner.Domain/
COPY src/TacticusPlanner.Persistence/TacticusPlanner.Persistence.csproj src/TacticusPlanner.Persistence/packages.lock.json src/TacticusPlanner.Persistence/
COPY src/TacticusPlanner.GameDomain/TacticusPlanner.GameDomain.csproj src/TacticusPlanner.GameDomain/packages.lock.json src/TacticusPlanner.GameDomain/
COPY orchestration/TacticusPlanner.ServiceDefaults/TacticusPlanner.ServiceDefaults.csproj orchestration/TacticusPlanner.ServiceDefaults/packages.lock.json orchestration/TacticusPlanner.ServiceDefaults/
RUN dotnet restore src/TacticusPlanner.Jobs/TacticusPlanner.Jobs.csproj --locked-mode

COPY src/TacticusPlanner.Jobs/ src/TacticusPlanner.Jobs/
COPY src/TacticusPlanner.Domain/ src/TacticusPlanner.Domain/
COPY src/TacticusPlanner.Persistence/ src/TacticusPlanner.Persistence/
COPY src/TacticusPlanner.GameDomain/ src/TacticusPlanner.GameDomain/
COPY orchestration/TacticusPlanner.ServiceDefaults/ orchestration/TacticusPlanner.ServiceDefaults/
RUN dotnet publish src/TacticusPlanner.Jobs/TacticusPlanner.Jobs.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# The ASP.NET Core runtime, not the plain one: ServiceDefaults (telemetry) references the ASP.NET Core framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1 AS runtime
WORKDIR /app
# Sized for the 0.25 vCPU / 0.5 GiB Container Apps Job (jobsCpu / jobsMemory in tacticus-planner-infra):
# one processor for the GC and thread pool, and a 320 MiB managed-heap ceiling well under the container limit.
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_PROCESSOR_COUNT=1 \
    DOTNET_GCHeapHardLimit=0x14000000 \
    DOTNET_GCConserveMemory=5 \
    MALLOC_ARENA_MAX=2
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
# The job name is the argument: the Container Apps Job passes it, and a bare `docker run` runs the smoke job.
ENTRYPOINT ["dotnet", "TacticusPlanner.Jobs.dll"]
CMD ["smoke"]
