using BookIt.Api.Authorization;
using BookIt.Api.Extensions;
using BookIt.Application.Dtos;
using BookIt.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(IBookingService bookingService, IAuthorizationService authorizationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest request)
    {
        var result = await bookingService.CreateAsync(User.GetUserId(), request);
        return result.Succeeded
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<BookingDto>>> GetMine() =>
        Ok(await bookingService.GetForUserAsync(User.GetUserId()));

    [HttpGet]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<List<BookingDto>>> GetAll() =>
        Ok(await bookingService.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> GetById(Guid id)
    {
        var result = await bookingService.GetByIdAsync(id);
        if (!result.Succeeded)
        {
            return NotFound(new { error = result.Error });
        }

        var authResult = await authorizationService.AuthorizeAsync(User, result.Value, PolicyNames.BookingOwnerOrAdmin);
        return authResult.Succeeded ? Ok(result.Value) : Forbid();
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<BookingDto>> Confirm(Guid id) =>
        await FromTransitionResult(await bookingService.ConfirmAsync(id));

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<BookingDto>> CheckIn(Guid id) =>
        await FromTransitionResult(await bookingService.CheckInAsync(id));

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<BookingDto>> Complete(Guid id) =>
        await FromTransitionResult(await bookingService.CompleteAsync(id));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(Guid id, CancelBookingRequest request)
    {
        var existing = await bookingService.GetByIdAsync(id);
        if (!existing.Succeeded)
        {
            return NotFound(new { error = existing.Error });
        }

        var authResult = await authorizationService.AuthorizeAsync(User, existing.Value, PolicyNames.BookingOwnerOrAdmin);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        return await FromTransitionResult(await bookingService.CancelAsync(id, request.Reason));
    }

    private Task<ActionResult<BookingDto>> FromTransitionResult(Application.Common.Result<BookingDto> result) =>
        Task.FromResult<ActionResult<BookingDto>>(result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error }));
}
