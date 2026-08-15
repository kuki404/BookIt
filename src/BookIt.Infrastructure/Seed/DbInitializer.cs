using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookIt.Infrastructure.Seed;

/// <summary>Applies pending migrations and seeds demo data on startup, so `docker compose up` + `dotnet run` from a fresh clone is enough to get a usable app — no manual seeding step.</summary>
public static class DbInitializer
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<BookItDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var roleName in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var adminEmail = "admin@bookit.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "Admin",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
            }
        }

        if (!await db.Resources.AnyAsync())
        {
            db.Resources.AddRange(
                Resource.Create("Conference Room A", ResourceType.Room, capacity: 8, "Main meeting room with a projector."),
                Resource.Create("Conference Room B", ResourceType.Room, capacity: 4, "Small room, good for 1:1s."),
                Resource.Create("Projector Cart", ResourceType.Equipment, capacity: 1, "Portable projector + screen."),
                Resource.Create("IT Support Slot", ResourceType.Service, capacity: 1, "30-minute IT support session.")
            );

            await db.SaveChangesAsync();
        }
    }
}
