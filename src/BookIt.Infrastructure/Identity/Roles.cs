namespace BookIt.Infrastructure.Identity;

/// <summary>Central place for role name constants so they're never typo'd across policies/seed data.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Customer];
}
