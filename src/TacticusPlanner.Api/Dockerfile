FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TacticusPlanner.Api/TacticusPlanner.Api.csproj src/TacticusPlanner.Api/packages.lock.json src/TacticusPlanner.Api/
COPY src/TacticusPlanner.BackgroundJobs/TacticusPlanner.BackgroundJobs.csproj src/TacticusPlanner.BackgroundJobs/packages.lock.json src/TacticusPlanner.BackgroundJobs/
COPY src/TacticusPlanner.Domain/TacticusPlanner.Domain.csproj src/TacticusPlanner.Domain/packages.lock.json src/TacticusPlanner.Domain/
COPY src/TacticusPlanner.Persistence/TacticusPlanner.Persistence.csproj src/TacticusPlanner.Persistence/packages.lock.json src/TacticusPlanner.Persistence/
COPY src/TacticusPlanner.GameCatalog/TacticusPlanner.GameCatalog.csproj src/TacticusPlanner.GameCatalog/packages.lock.json src/TacticusPlanner.GameCatalog/
COPY src/TacticusPlanner.GameDomain/TacticusPlanner.GameDomain.csproj src/TacticusPlanner.GameDomain/packages.lock.json src/TacticusPlanner.GameDomain/
COPY src/TacticusPlanner.TacticusApi/TacticusPlanner.TacticusApi.csproj src/TacticusPlanner.TacticusApi/packages.lock.json src/TacticusPlanner.TacticusApi/
COPY orchestration/TacticusPlanner.ServiceDefaults/TacticusPlanner.ServiceDefaults.csproj orchestration/TacticusPlanner.ServiceDefaults/packages.lock.json orchestration/TacticusPlanner.ServiceDefaults/
RUN dotnet restore src/TacticusPlanner.Api/TacticusPlanner.Api.csproj --locked-mode

COPY src/TacticusPlanner.Api/ src/TacticusPlanner.Api/
COPY src/TacticusPlanner.BackgroundJobs/ src/TacticusPlanner.BackgroundJobs/
COPY src/TacticusPlanner.Domain/ src/TacticusPlanner.Domain/
COPY src/TacticusPlanner.Persistence/ src/TacticusPlanner.Persistence/
COPY src/TacticusPlanner.GameCatalog/ src/TacticusPlanner.GameCatalog/
COPY src/TacticusPlanner.GameDomain/ src/TacticusPlanner.GameDomain/
COPY src/TacticusPlanner.TacticusApi/ src/TacticusPlanner.TacticusApi/
COPY orchestration/TacticusPlanner.ServiceDefaults/ orchestration/TacticusPlanner.ServiceDefaults/
RUN dotnet publish src/TacticusPlanner.Api/TacticusPlanner.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1 AS runtime
WORKDIR /app
# Runtime sized for the 0.5 vCPU / 1 GiB Container App (containerCpu / containerMemory in tacticus-planner-infra):
# - DOTNET_PROCESSOR_COUNT=1: the runtime otherwise sees the node's 4 CPUs and sizes the GC and thread pool for them.
# - DOTNET_GCHeapHardLimit=0x28000000 (640 MiB): an explicit managed-heap ceiling, so the GC compacts and trims well
#   before the container limit instead of the replica hanging. Revisit it if containerMemory changes.
# - DOTNET_GCConserveMemory=5: compact gen2 when it is fragmented instead of growing the heap.
# - MALLOC_ARENA_MAX=2: cap glibc malloc arenas, which otherwise fragment native memory across threads.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_PROCESSOR_COUNT=1 \
    DOTNET_GCHeapHardLimit=0x28000000 \
    DOTNET_GCConserveMemory=5 \
    MALLOC_ARENA_MAX=2
EXPOSE 8080
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "TacticusPlanner.Api.dll"]
