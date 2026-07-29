FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/TacticusPlanner.Api/TacticusPlanner.Api.csproj src/TacticusPlanner.Api/packages.lock.json src/TacticusPlanner.Api/
COPY src/TacticusPlanner.Domain/TacticusPlanner.Domain.csproj src/TacticusPlanner.Domain/packages.lock.json src/TacticusPlanner.Domain/
COPY src/TacticusPlanner.Persistence/TacticusPlanner.Persistence.csproj src/TacticusPlanner.Persistence/packages.lock.json src/TacticusPlanner.Persistence/
COPY src/TacticusPlanner.GameCatalog/TacticusPlanner.GameCatalog.csproj src/TacticusPlanner.GameCatalog/packages.lock.json src/TacticusPlanner.GameCatalog/
COPY src/TacticusPlanner.GameDomain/TacticusPlanner.GameDomain.csproj src/TacticusPlanner.GameDomain/packages.lock.json src/TacticusPlanner.GameDomain/
COPY src/TacticusPlanner.TacticusApi/TacticusPlanner.TacticusApi.csproj src/TacticusPlanner.TacticusApi/packages.lock.json src/TacticusPlanner.TacticusApi/
COPY orchestration/TacticusPlanner.ServiceDefaults/TacticusPlanner.ServiceDefaults.csproj orchestration/TacticusPlanner.ServiceDefaults/packages.lock.json orchestration/TacticusPlanner.ServiceDefaults/
RUN dotnet restore src/TacticusPlanner.Api/TacticusPlanner.Api.csproj --locked-mode

COPY src/TacticusPlanner.Api/ src/TacticusPlanner.Api/
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "TacticusPlanner.Api.dll"]
