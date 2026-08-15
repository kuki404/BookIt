using System.Text;
using BookIt.Api.Authorization;
using BookIt.Application.Abstractions;
using BookIt.Application.Services;
using BookIt.Infrastructure;
using BookIt.Infrastructure.Auth;
using BookIt.Infrastructure.BackgroundJobs;
using BookIt.Infrastructure.Email;
using BookIt.Infrastructure.Identity;
using BookIt.Infrastructure.Repositories;
using BookIt.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var connectionString = SqlConnectionStringFactory.Build(builder.Configuration);
builder.Services.AddDbContext<BookItDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

// --- Identity (API-only: no cookie scheme, JWT is the sole authentication scheme) ---
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BookItDbContext>();

// --- JWT authentication ---
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Jwt:Secret is not configured. Set it with 'dotnet user-secrets set \"Jwt:Secret\" \"<a long random string>\" --project src/BookIt.Api'.");

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
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(PolicyNames.AdminOnly, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(PolicyNames.BookingOwnerOrAdmin, policy => policy.Requirements.Add(new SameOwnerOrAdminRequirement()));

builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, BookingOwnerAuthorizationHandler>();

// --- Application services ---
builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IResourceService, ResourceService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<BookingReminderService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.RunAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so WebApplicationFactory<Program> can be used from integration tests.
public partial class Program;
