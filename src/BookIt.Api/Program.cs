using System.Text;
using System.Threading.RateLimiting;
using BookIt.Api.Authorization;
using BookIt.Api.Middleware;
using BookIt.Application.Abstractions;
using BookIt.Application.Services;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Auth;
using BookIt.Infrastructure.BackgroundJobs;
using BookIt.Infrastructure.Caching;
using BookIt.Infrastructure.Email;
using BookIt.Infrastructure.Identity;
using BookIt.Infrastructure.Seed;
using BookIt.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
// Pooled: EF Core reuses DbContext instances across requests instead of allocating one per
// request, which matters once request volume is high enough for allocation/GC to show up.
var connectionString = SqlConnectionStringFactory.Build(builder.Configuration);
builder.Services.AddDbContextPool<BookItDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
        // Query text (not parameter values) in logs/exceptions — off by default and never
        // enabled outside Development, since it can leak PII from bound parameter values.
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment()));

// --- Identity (API-only: no cookie scheme, JWT is the sole authentication scheme) ---
// AddSignInManager pulls in SignInManager without the rest of AddIdentity's cookie-auth wiring,
// which would otherwise register a second (unused) authentication scheme.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;

        // Brute-force protection: after 5 wrong passwords, the account is locked for 15 minutes —
        // enforced by SignInManager.CheckPasswordSignInAsync in AuthController, not by this
        // config alone.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BookItDbContext>()
    .AddSignInManager();

// --- JWT authentication ---
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Jwt:Secret is not configured. Set it with 'dotnet user-secrets set \"Jwt:Secret\" \"<a long random string>\" --project src/BookIt.Api'.");
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    // HMAC-SHA256 wants a >=256-bit key; a short secret would be brute-forceable offline from a
    // single captured token. Fail fast at startup rather than silently signing with a weak key.
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters (256 bits) long.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // Pin the accepted algorithm — without this, a token forged with e.g. "alg: none" or
            // a different algorithm the key material also happens to validate under could pass.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.AdminOnly, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(PolicyNames.BookingOwnerOrAdmin, policy => policy.Requirements.Add(new SameOwnerOrAdminRequirement()))
    // Secure by default: any endpoint without an explicit [Authorize]/[AllowAnonymous] requires a
    // valid token. A new controller added later can't accidentally ship wide open.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddScoped<IAuthorizationHandler, BookingOwnerAuthorizationHandler>();

// --- Rate limiting ---
// "auth" is deliberately strict (login/register/refresh are the highest-value brute-force/abuse
// targets); everything else falls back to a much looser global per-IP limit.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Configurable so integration tests (BookItWebApplicationFactory) can raise this limit —
    // otherwise a fast test run legitimately trips the same brute-force protection it's meant to
    // test, rather than that being a real signal of abuse.
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:Auth:PermitLimit", 5);
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = authPermitLimit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// --- Caching ---
// HybridCache: data-layer cache in ResourceService (L1 in-memory, stampede-protected,
// tag-invalidated on write) — saves the DB round trip.
builder.Services.AddHybridCache();
builder.Services.AddSingleton<ResourceCache>();
// OutputCache: response-layer cache for the anonymous resource listing — saves the controller +
// JSON serialization work entirely on a hit, on top of whatever HybridCache saves underneath.
builder.Services.AddOutputCache();

// --- Application services (talk to BookItDbContext directly — no repository layer) ---
builder.Services.AddScoped<IResourceService, ResourceService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<BookingReminderService>();

// --- Error handling: every unhandled exception becomes RFC 9457 ProblemDetails, never a raw
// stack trace, in every environment (not just non-Development). ---
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// --- CORS: only the Web app's own origin may call this API from a browser. Blazor Server itself
// doesn't need this (its HttpClient calls happen server-side, not from JS), but it's cheap
// insurance against this API being called from an arbitrary page in someone's browser. ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy("WebClient", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// --- Health checks: /health/live (process is up, no dependencies) vs /health/ready (can actually
// serve traffic — DB reachable). Docker's healthcheck (docker-compose.yml) probes /health/ready. ---
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BookItDbContext>(name: "database", tags: ["ready"]);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.RunAsync(scope.ServiceProvider);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// HSTS tells the browser to only ever reach this host over HTTPS for the given duration, closing
// the window where a plain-HTTP first request could be intercepted before the redirect below
// even happens. Development-only excluded: enabling it there pins localhost to HTTPS in the
// browser's HSTS cache, breaking plain-HTTP local debugging in a way that outlives the app run.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security headers on every response — belt-and-suspenders alongside HSTS; cheap and this API has
// no reason to ever be framed, sniffed as a different content-type, or leak the referrer.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    await next();
});

app.UseCors("WebClient");

app.UseRateLimiter();
app.UseOutputCache();

app.UseAuthentication();
app.UseAuthorization();

// The "auth" fixed-window policy is applied per-controller via [EnableRateLimiting("auth")] on
// AuthController; every other endpoint just gets the GlobalLimiter registered above.
app.MapControllers();

// Predicate: _ => false runs zero checks — liveness only answers "is the process responding at
// all", it must never depend on the database being reachable. Both are unauthenticated: an
// orchestrator's health prober has no JWT to send.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

// Exposed so WebApplicationFactory<Program> can be used from integration tests.
public partial class Program;
