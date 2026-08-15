# Multi-stage build following the official dotnet/dotnet-docker sample pattern.
# Build context is the repo root because Web references BookIt.Application/BookIt.Domain.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BookIt.Domain/BookIt.Domain.csproj src/BookIt.Domain/
COPY src/BookIt.Application/BookIt.Application.csproj src/BookIt.Application/
COPY src/BookIt.Web/BookIt.Web.csproj src/BookIt.Web/
RUN dotnet restore src/BookIt.Web/BookIt.Web.csproj

COPY src/BookIt.Domain/ src/BookIt.Domain/
COPY src/BookIt.Application/ src/BookIt.Application/
COPY src/BookIt.Web/ src/BookIt.Web/
RUN dotnet publish src/BookIt.Web/BookIt.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookIt.Web.dll"]
