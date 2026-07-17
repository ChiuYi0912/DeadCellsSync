# Headless dedicated server image. Only NetcodeArena.Server (+ its NetcodeArena.Core
# dependency) is published here - no test project, no Unity, nothing that isn't needed to run
# the authoritative simulation.

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY NetcodeArena.sln ./
COPY src/NetcodeArena.Core/NetcodeArena.Core.csproj src/NetcodeArena.Core/
COPY src/NetcodeArena.Server/NetcodeArena.Server.csproj src/NetcodeArena.Server/
COPY tests/NetcodeArena.Core.Tests/NetcodeArena.Core.Tests.csproj tests/NetcodeArena.Core.Tests/
RUN dotnet restore src/NetcodeArena.Server/NetcodeArena.Server.csproj

COPY src/ src/
RUN dotnet publish src/NetcodeArena.Server/NetcodeArena.Server.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Authoritative UDP tick loop - see SimulationConfig.TicksPerSecond / SnapshotRateHz.
EXPOSE 7777/udp

ENTRYPOINT ["dotnet", "NetcodeArena.Server.dll", "7777"]
