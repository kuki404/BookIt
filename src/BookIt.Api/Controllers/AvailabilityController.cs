using BookIt.Application.Dtos;
using BookIt.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/availability")]
[AllowAnonymous]
public class AvailabilityController(IBookingService bookingService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AvailabilityResponse>> Get([FromQuery] Guid resourceId, [FromQuery] DateOnly date)
    {
        var result = await bookingService.GetAvailabilityAsync(resourceId, date);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }
}
