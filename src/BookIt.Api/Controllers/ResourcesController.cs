using BookIt.Api.Authorization;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace BookIt.Api.Controllers;

[ApiController]
[Route("api/resources")]
[Authorize]
public class ResourcesController(IResourceService resourceService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    // Public catalog, identical response for every caller — a good fit for response caching.
    // VaryByQuery so ?includeInactive=/page=/pageSize= each get their own cached entry rather
    // than colliding on one. Invalidated indirectly: the underlying HybridCache entry this reads
    // from is tag-invalidated on write, and this response expires on its own short timer.
    [OutputCache(Duration = 60, VaryByQueryKeys = ["includeInactive", "page", "pageSize"])]
    public async Task<ActionResult<PagedResult<ResourceDto>>> GetAll([FromQuery] bool includeInactive, [FromQuery] PagedRequest paging) =>
        Ok(await resourceService.GetAllAsync(includeInactive, paging));

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
