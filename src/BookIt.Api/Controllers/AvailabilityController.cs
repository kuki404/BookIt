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

    [HttpGet("range")]
    public async Task<ActionResult<AvailabilityRangeResponse>> GetRange([FromQuery] Guid resourceId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate)
    {
        var result = await bookingService.GetAvailabilityRangeAsync(resourceId, startDate, endDate);
        if (result.Succeeded)
        {
            return Ok(result.Value);
        }

        return result.Error == "Resource not found."
            ? NotFound(new { error = result.Error })
            : BadRequest(new { error = result.Error });
    }
}
