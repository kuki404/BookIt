# BookIt

[![CI](https://github.com/kuki404/BookIt/actions/workflows/ci.yml/badge.svg)](https://github.com/kuki404/BookIt/actions/workflows/ci.yml)

A resource booking system — rooms, equipment or services, booked by time slot, with double-booking
prevention, brute-force-hardened auth, and a Blazor UI on top of a proper Web API.

Built as a portfolio project to demonstrate: ASP.NET Core Web API (Controllers/Actions), EF Core +
SQL Server modeling (concurrency, transactions, projections, paging, caching), JWT auth with
refresh-token rotation and reuse detection, policy- and resource-based authorization, rate
limiting, and a Blazor Server + MudBlazor front end — all on official Microsoft NuGet packages,
plus two named exceptions: **MudBlazor** (UI kit) and **Mapster** (DTO → view-model mapping).

## Try it in 60 seconds

```bash
git clone https://github.com/kuki404/BookIt.git && cd BookIt
cp .env.example .env   # edit the two values inside — see "First-time setup" below
docker compose up -d --build
```

Open **http://localhost:5232**, click **Log in**, then **Demo Admin** or **Demo Customer** — no
typing required, both accounts are seeded automatically on first run.

## Architecture

```mermaid
flowchart LR
    Browser["Browser"] -- SignalR circuit --> Web["BookIt.Web<br/>Blazor Server + MudBlazor"]
    Web -- "typed HttpClient<br/>(JWT bearer)" --> Api["BookIt.Api<br/>Controllers + JWT auth"]
    Api --> App["BookIt.Application<br/>DTOs · Result&lt;T&gt; · service contracts"]
    Api --> Infra["BookIt.Infrastructure<br/>services · DbContext · Identity · cache"]
    Infra --> Domain["BookIt.Domain<br/>entities · state machines"]
    Infra --> Db[("SQL Server 2025")]
    Infra --> Cache[("HybridCache<br/>in-memory")]
```

No repository layer: `BookIt.Infrastructure/Services` talks to `BookItDbContext` directly and
projects straight into `BookIt.Application` DTOs — the `DbSet` already *is* a repository and the
`DbContext` already *is* a unit of work, so a wrapping interface would only hide the EF Core
features (`AsNoTracking`, SQL-side projection, paging) the code relies on.

## Security

| Concern | How it's handled |
|---|---|
| Brute-force login | `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)` — 5 failed attempts locks the account 15 minutes (`AuthController.cs`, `Program.cs`) |
| Stolen refresh token | Rotated on every use; **reuse of an already-rotated token revokes every active session for that user**, not just the replayed one (`AuthController.Refresh`) |
| Brute-force / abuse generally | `/api/auth/*` rate-limited to 5 requests/min/IP; everything else to 200/min/IP (`AddRateLimiter`, `Program.cs`) |
| New endpoint added without thinking about auth | `FallbackPolicy = RequireAuthenticatedUser()` — locked by default, `[AllowAnonymous]` is opt-in, not opt-out |
| Ownership | Resource-based `IAuthorizationHandler` — a Customer token can list/act on *their own* bookings only; role alone isn't enough (`BookingOwnerAuthorizationHandler.cs`) |
| Errors leaking internals | Every unhandled exception → RFC 9457 `ProblemDetails` with a `traceId`, never a stack trace, in any environment (`GlobalExceptionHandler.cs`) |
| Forged/weak JWTs | Pinned to `HmacSha256` only; refuses to start if `Jwt:Secret` is under 256 bits (`Program.cs`) |

## Performance & caching

- **SQL-side projection everywhere**: list/detail queries `.Select()` straight into a DTO
  (`BookingProjections`/`ResourceProjections` in `BookIt.Application/Mapping`) — EF Core generates
  a `SELECT` with only the needed columns, never "load the entity, map it in C# after." Verified:
  ```
  SELECT [r].[Id], [r].[Name], [r].[Description], [r].[Type], [r].[Capacity], [r].[IsActive]
  FROM [Resources] AS [r]
  WHERE [r].[IsActive] = CAST(1 AS bit)
  ORDER BY [r].[Name]
  OFFSET @p ROWS FETCH NEXT @p1 ROWS ONLY
  ```
- **Paging is enforced, not just offered**: `PagedRequest.PageSize` is capped at 100
  server-side — `GET /api/resources?pageSize=1000` is a `400`, not a truncated 1000-row response.
- **`HybridCache`** (official, GA .NET 9+) fronts the resource catalog, tag-invalidated on every
  write — no stale-TTL guessing. **`OutputCache`** sits in front of that for the anonymous listing
  endpoint. Verified with two identical requests:
  ```
  # first call — cache miss, hits the DB
  # second call — served from cache:
  < Age: 0
  ```
- **One compiled query** (`EF.CompileAsyncQuery`) for the overlap check — the single query that
  runs on *every* booking attempt, including retries under lock contention.
- Filtered composite indexes match the exact predicates the hot queries use (see comments in
  `BookIt.Infrastructure/Configurations/*.cs`), and `AddDbContextPool` reuses `DbContext`
  instances instead of allocating one per request.

## Frontend

- Custom teal/indigo MudBlazor theme with a dark/light toggle — starts from the OS preference,
  remembered afterwards (`ThemeService.cs`, `ProtectedLocalStorage`).
- Session survives a hard refresh: the JWT is mirrored into `ProtectedSessionStorage` (encrypted
  via ASP.NET Core Data Protection), restored on the very first render of a new circuit
  (`AuthSession.cs`).
- Skeleton loading states, empty states with a call to action, and a page-level `ErrorBoundary` so
  one broken component doesn't take down the whole circuit.
- Accessibility: skip-to-content link, `aria-label` on every icon-only button, keyboard-navigable
  throughout.
- SEO: per-page `<PageTitle>`/`<HeadContent>` meta description, `robots.txt`, `sitemap.xml`,
  authenticated pages marked `noindex`.

## Project layout

```
src/
  BookIt.Domain/          Entities, enums, rich domain methods (Booking.Confirm()/Cancel()/...) —
                           no EF Core / ASP.NET dependency
  BookIt.Application/     DTOs, Result<T>/PagedResult<T>, service contracts, SQL-projection
                           expressions — no EF Core dependency either, so Web can reference it
  BookIt.Infrastructure/  DbContext, migrations, service implementations (DbContext injected
                           directly — no repositories), Identity, JWT issuing, HybridCache,
                           background jobs
  BookIt.Api/             Web API — Controllers, JWT/rate-limiting/CORS/ProblemDetails wiring,
                           authorization policies + resource-based handler, health checks
  BookIt.Web/             Blazor Server + MudBlazor + Mapster view-models, typed HttpClient
tests/
  BookIt.UnitTests/        Domain state-machine tests (xUnit)
  BookIt.IntegrationTests/ WebApplicationFactory tests against a real SQL Server (xUnit) —
                           auth flow, lockout, refresh-token reuse detection, resource-based 403s
```

Central Package Management (`Directory.Packages.props`) pins every NuGet version in one place;
`Directory.Build.props` turns on .NET analyzers solution-wide with warnings treated as errors.

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

4. Apply migrations (the app also auto-migrates and seeds demo accounts + resources on startup, so
   this is only needed if you want the schema ready before first run):
   ```bash
   dotnet ef database update --project src/BookIt.Infrastructure --startup-project src/BookIt.Api
   ```

5. Run both apps (separate terminals):
   ```bash
   dotnet run --project src/BookIt.Api    # http://localhost:5098
   dotnet run --project src/BookIt.Web    # http://localhost:5232
   ```

Seeded logins (also available as one-click buttons on the Login page):
**admin@bookit.local / Admin123!** and **customer@bookit.local / Customer123!**

> **Why User Secrets and not `.env` for the app itself?** Docker Compose reads `.env` natively —
> no code needed. The .NET apps read config from [User
> Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), Microsoft's own
> mechanism for keeping local secrets out of source control, so no extra NuGet package is needed
> to parse a `.env` file from .NET code. Both end up holding the same password; that's expected.

## Running fully in Docker (no local .NET needed to run it)

```bash
docker compose up -d --build
```

Builds and starts **all three services** — `db`, `api`, `web` — from a clean clone, each with a
`HEALTHCHECK` (`/health/ready` on the Api, `/health` on Web) so `depends_on: condition:
service_healthy` actually waits for the app to be able to serve traffic, not just for the process
to start. Same URLs as above.

> SDKs are pinned to an exact patch (`10.0.302`) in both Dockerfiles, not the floating `10.0` tag —
> a later SDK patch was found mid-build to silently drop the Blazor Server client runtime
> (`wwwroot/_framework/blazor.web.js`) from a multi-project publish, breaking all interactivity
> with no build error. Floating tags on a newly-released major version are exactly where that kind
> of regression bites.

## Tests

```bash
docker compose up -d db          # integration tests need a live SQL Server
dotnet test
```

23 tests: domain state-machine unit tests, plus integration tests covering the full auth flow,
account lockout after 5 failed logins, refresh-token reuse detection, resource-based 403s, and
server-enforced pagination limits — all against a separate `BookIt_IntegrationTests` database on
the same SQL Server container (never touches your dev data).

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
