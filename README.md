# BookIt

A resource booking system — rooms, equipment or services, booked by time slot, with double-booking
prevention, role-based + resource-based authorization, and a Blazor UI on top of a proper Web API.

Built as a portfolio project to demonstrate: ASP.NET Core Web API (Controllers/Actions), EF Core +
SQL Server modeling (concurrency tokens, transactions, overlap queries), JWT authentication with
refresh-token rotation, policy-based and resource-based authorization, background jobs, and a
Blazor Server + MudBlazor front end — all built exclusively on official Microsoft NuGet packages
(plus MudBlazor for the UI component library).

## Stack

- .NET 10 — ASP.NET Core Web API (`BookIt.Api`) + Blazor Server (`BookIt.Web`, MudBlazor)
- EF Core 10 + SQL Server 2025 (Docker)
- ASP.NET Core Identity + hand-rolled JWT (access + rotating refresh tokens)
- No third-party application packages — validation is `DataAnnotations`, background jobs are a
  plain `BackgroundService`, email is the built-in `SmtpClient`. See [Architecture](#architecture).

## Project layout

```
src/
  BookIt.Domain/          Entities, enums, rich domain methods (e.g. Booking.Confirm()) — no
                           EF Core / ASP.NET dependency
  BookIt.Application/     DTOs, Result<T>, service interfaces + implementations (business logic)
  BookIt.Infrastructure/  EF Core DbContext, migrations, repositories, Identity, JWT issuing,
                           background reminder job
  BookIt.Api/             Web API — Controllers, JWT auth wiring, authorization policies/handlers
  BookIt.Web/              Blazor Server + MudBlazor, calls the Api via a typed HttpClient
tests/
  BookIt.UnitTests/        Domain state-machine tests (xUnit)
  BookIt.IntegrationTests/ WebApplicationFactory tests against a real SQL Server (xUnit)
```

## Architecture

- **Authorization**: ASP.NET Core Identity (`User`/`Role`) issues JWT access tokens (15 min) and
  rotating refresh tokens (30 days, hashed in the DB, revoked on every use). `AdminOnly` is
  policy-based; booking cancel/view is **resource-based** — a custom `IAuthorizationHandler`
  checks the actual booking's owner, so one customer can never act on another's booking even
  though both hold the same role.
- **Concurrency**: creating a booking runs an overlap check + insert inside a `Serializable`
  transaction (via EF Core's execution strategy, since `EnableRetryOnFailure` requires it) so two
  concurrent requests for the same resource/time-slot can't both succeed. `Booking.RowVersion` is
  an optimistic concurrency token for updates.
- **Domain model**: `Booking` is a rich entity — `Confirm()/CheckIn()/Complete()/Cancel()` enforce
  a state machine and throw `DomainException` on an invalid transition, instead of leaving that
  logic to whichever controller action happens to touch the entity.
- **Background job**: `BookingReminderService` (`Microsoft.Extensions.Hosting.BackgroundService` +
  `PeriodicTimer`) sweeps every 5 minutes for confirmed bookings starting soon and emails a
  reminder via the built-in `SmtpClient` — no Hangfire/Quartz dependency.
- **Validation**: `DataAnnotations` on request DTOs, including `IValidatableObject` for
  cross-field rules (e.g. start < end) — validated automatically by `[ApiController]`, no
  FluentValidation dependency.
- **API docs**: built-in `Microsoft.AspNetCore.OpenApi` — the raw document is served at
  `/openapi/v1.json` in Development (no Swagger UI/Scalar dependency); import it into Postman or a
  VS Code REST client to explore the API interactively.

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`

## First-time setup (running locally with `dotnet run`)

1. Copy `.env.example` to `.env` and fill in your own values:
   ```
   MSSQL_SA_PASSWORD=your-password
   JWT_SECRET=a-long-random-string-at-least-32-characters
   ```
   `.env` is gitignored and never committed — this is what makes the database yours alone, not
   shared with anyone else who clones the repo.

2. Start the database:
   ```bash
   docker compose up -d db
   ```

3. Point the app at your own secrets (User Secrets, not `.env` — see note below):
   ```bash
   dotnet user-secrets set "Sql:Password" "<same password as MSSQL_SA_PASSWORD>" --project src/BookIt.Api
   dotnet user-secrets set "Jwt:Secret" "<same value as JWT_SECRET>" --project src/BookIt.Api
   ```

4. Apply migrations (the app also auto-migrates and seeds an admin user + demo resources on
   startup, so this is only needed if you want the schema ready before first run):
   ```bash
   dotnet ef database update --project src/BookIt.Infrastructure --startup-project src/BookIt.Api
   ```

5. Run both apps (separate terminals):
   ```bash
   dotnet run --project src/BookIt.Api    # http://localhost:5098
   dotnet run --project src/BookIt.Web    # http://localhost:5232
   ```

Seeded login: **admin@bookit.local / Admin123!** (Admin role). Register your own account from the
UI to get a Customer-role account.

> **Why User Secrets and not `.env` for the app itself?** Docker Compose reads `.env` natively —
> no code needed. The .NET apps read config from [User
> Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), Microsoft's own
> mechanism for keeping local secrets out of source control, so no extra NuGet package is needed
> to parse a `.env` file from .NET code. Both end up holding the same password; that's expected.

## Running fully in Docker (no local .NET needed to run it)

```bash
docker compose up -d --build
```

This builds and starts **all three services** — `db`, `api`, `web` — from a clean clone. The Api
container reads `Sql__Password`/`Jwt__Secret` from the same `.env` file via Compose environment
variables (no User Secrets involved in the container). Same URLs as above.

## Tests

```bash
docker compose up -d db          # integration tests need a live SQL Server
dotnet test
```

Integration tests run against a separate `BookIt_IntegrationTests` database on the same SQL Server
container (never touches your dev data) and reuse the User Secrets set up above.

## CI/CD

- **`.github/workflows/ci.yml`** — actually runs on this repo's GitHub Actions: restore, build,
  unit + integration tests (against a real SQL Server service container), then builds both Docker
  images.
- **`azure-pipelines.yml`** — the same stages in Azure DevOps YAML syntax. Not wired to a live
  Azure DevOps project; drop it into Pipelines → New pipeline → Existing YAML file to activate it.

## Notes

- The SQL Server image is `amd64`; on Apple Silicon it runs emulated via Rosetta — expect a
  slower cold start. `EnableRetryOnFailure()` smooths over transient connection hiccups.
- The database only listens on `127.0.0.1:1433` (see `docker-compose.yml`) — reachable from this
  machine only, never exposed to the local network.
