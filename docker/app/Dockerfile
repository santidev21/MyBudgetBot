# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Restore is isolated in its own layer so it is only re-run when a project file
# or a package version changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/MyBudget.Domain/MyBudget.Domain.csproj src/MyBudget.Domain/
COPY src/MyBudget.Application/MyBudget.Application.csproj src/MyBudget.Application/
COPY src/MyBudget.Infrastructure/MyBudget.Infrastructure.csproj src/MyBudget.Infrastructure/
COPY src/MyBudget.Telegram/MyBudget.Telegram.csproj src/MyBudget.Telegram/
COPY src/MyBudget.Api/MyBudget.Api.csproj src/MyBudget.Api/
RUN dotnet restore src/MyBudget.Api/MyBudget.Api.csproj

COPY src/ src/
RUN dotnet publish src/MyBudget.Api/MyBudget.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime
#
# Debian rather than Alpine on purpose:
#   * full ICU, which es-CO monetary and date formatting depends on;
#   * tzdata, which TimeZoneInfo needs to resolve America/Bogota at runtime.
# Alpine needs both bolted on and every miss is a runtime formatting bug.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install --yes --no-install-recommends \
        curl \
        tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# Liveness only, so a database outage never triggers a restart loop.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/health/live || exit 1

# The base image defines APP_UID (1654): the process never runs as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "MyBudget.Api.dll"]
