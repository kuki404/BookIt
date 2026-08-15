# Multi-stage build following the official dotnet/dotnet-docker sample pattern:
# https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile
# Build context is the repo root (not this folder) because the Api project references
# sibling projects (Domain/Application/Infrastructure) outside src/BookIt.Api.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BookIt.Domain/BookIt.Domain.csproj src/BookIt.Domain/
COPY src/BookIt.Application/BookIt.Application.csproj src/BookIt.Application/
COPY src/BookIt.Infrastructure/BookIt.Infrastructure.csproj src/BookIt.Infrastructure/
COPY src/BookIt.Api/BookIt.Api.csproj src/BookIt.Api/
RUN dotnet restore src/BookIt.Api/BookIt.Api.csproj

COPY src/BookIt.Domain/ src/BookIt.Domain/
COPY src/BookIt.Application/ src/BookIt.Application/
COPY src/BookIt.Infrastructure/ src/BookIt.Infrastructure/
COPY src/BookIt.Api/ src/BookIt.Api/
RUN dotnet publish src/BookIt.Api/BookIt.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookIt.Api.dll"]
