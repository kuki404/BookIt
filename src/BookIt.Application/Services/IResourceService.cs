using BookIt.Application.Common;
using BookIt.Application.Dtos;

namespace BookIt.Application.Services;

public interface IResourceService
{
    ValueTask<PagedResult<ResourceDto>> GetAllAsync(bool includeInactive, PagedRequest paging, CancellationToken cancellationToken = default);
    Task<Result<ResourceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ResourceDto> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken = default);
    Task<Result<ResourceDto>> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
