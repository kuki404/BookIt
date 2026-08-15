# Multi-stage build following the official dotnet/dotnet-docker sample pattern.
# Build context is the repo root because Web references BookIt.Application/BookIt.Domain.
#
# SDK pinned to an exact patch (not the floating "10.0" tag): SDK 10.0.400 was found to silently
# drop wwwroot/_framework/blazor.web.js from a multi-project Blazor Server publish (present with
# 10.0.302), breaking all client interactivity with no build-time error — floating tags on a very
# recently released major version are exactly where that kind of regression bites hardest.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

# Central Package Management: restore needs these at the repo root before it can resolve any
# PackageReference's version.
COPY Directory.Packages.props Directory.Build.props ./

COPY src/BookIt.Domain/BookIt.Domain.csproj src/BookIt.Domain/
COPY src/BookIt.Application/BookIt.Application.csproj src/BookIt.Application/
COPY src/BookIt.Web/BookIt.Web.csproj src/BookIt.Web/
RUN dotnet restore src/BookIt.Web/BookIt.Web.csproj

COPY src/BookIt.Domain/ src/BookIt.Domain/
COPY src/BookIt.Application/ src/BookIt.Application/
COPY src/BookIt.Web/ src/BookIt.Web/
# No --no-restore: with only the .csproj files present, the earlier restore layer builds an
# incomplete static-web-assets graph for a Blazor project with cross-project references — it
# silently drops the framework's own wwwroot/_framework/blazor.web.js, breaking all interactivity
# with no build error. Re-running restore here (fast — NuGet packages are already cached from the
# layer above) rebuilds that graph against the full source and produces the correct output.
RUN dotnet publish src/BookIt.Web/BookIt.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl for the HEALTHCHECK below — the base runtime image doesn't include it.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "BookIt.Web.dll"]
