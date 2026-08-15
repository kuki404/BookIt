using BookIt.Api.Authorization;
using BookIt.Application.Dtos;
using BookIt.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/resources")]
[Authorize]
public class ResourcesController(IResourceService resourceService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<ResourceDto>>> GetAll([FromQuery] bool includeInactive = false) =>
        Ok(await resourceService.GetAllAsync(includeInactive));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ResourceDto>> GetById(Guid id)
    {
        var result = await resourceService.GetByIdAsync(id);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<ResourceDto>> Create(CreateResourceRequest request)
    {
        var created = await resourceService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<ActionResult<ResourceDto>> Update(Guid id, UpdateResourceRequest request)
    {
        var result = await resourceService.UpdateAsync(id, request);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await resourceService.DeactivateAsync(id);
        return result.Succeeded ? NoContent() : NotFound(new { error = result.Error });
    }
}
