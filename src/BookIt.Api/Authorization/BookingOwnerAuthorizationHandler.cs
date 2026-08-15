using System.Security.Claims;
using BookIt.Application.Dtos;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace BookIt.Api.Authorization;

public class SameOwnerOrAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Resource-based authorization: a booking can be acted on by the user who owns it, or by an
/// Admin — checked against the actual BookingDto, not just the caller's role, so "customer A
/// cancels customer B's booking" is rejected even though both hold the Customer role.
/// </summary>
public class BookingOwnerAuthorizationHandler : AuthorizationHandler<SameOwnerOrAdminRequirement, BookingDto>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameOwnerOrAdminRequirement requirement,
        BookingDto resource)
    {
        if (context.User.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        if (Guid.TryParse(userIdClaim, out var userId) && userId == resource.UserId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
