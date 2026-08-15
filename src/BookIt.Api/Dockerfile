# Multi-stage build following the official dotnet/dotnet-docker sample pattern:
# https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile
# Build context is the repo root (not this folder) because the Api project references
# sibling projects (Domain/Application/Infrastructure) outside src/BookIt.Api.
#
# SDK pinned to an exact patch (see BookIt.Web/Dockerfile for why: 10.0.400 broke Blazor Server
# publish output) — pinned here too for a reproducible build even though the Api has no razor
# components of its own.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# Central Package Management: restore needs these at the repo root before it can resolve any
# PackageReference's version.
COPY Directory.Packages.props Directory.Build.props ./

COPY src/BookIt.Domain/BookIt.Domain.csproj src/BookIt.Domain/
COPY src/BookIt.Application/BookIt.Application.csproj src/BookIt.Application/
COPY src/BookIt.Infrastructure/BookIt.Infrastructure.csproj src/BookIt.Infrastructure/
COPY src/BookIt.Api/BookIt.Api.csproj src/BookIt.Api/
RUN dotnet restore src/BookIt.Api/BookIt.Api.csproj

COPY src/BookIt.Domain/ src/BookIt.Domain/
COPY src/BookIt.Application/ src/BookIt.Application/
COPY src/BookIt.Infrastructure/ src/BookIt.Infrastructure/
COPY src/BookIt.Api/ src/BookIt.Api/
# No --no-restore — see BookIt.Web/Dockerfile for why: restoring only against .csproj files (the
# layer above) can produce a stale/incomplete asset graph for the full source tree.
RUN dotnet publish src/BookIt.Api/BookIt.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl for the HEALTHCHECK below — the base runtime image doesn't include it.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -f http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "BookIt.Api.dll"]
