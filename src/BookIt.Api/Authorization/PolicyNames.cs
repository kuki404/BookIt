namespace BookIt.Api.Authorization;

public static class PolicyNames
{
    public const string AdminOnly = "AdminOnly";
    public const string BookingOwnerOrAdmin = "BookingOwnerOrAdmin";
}
