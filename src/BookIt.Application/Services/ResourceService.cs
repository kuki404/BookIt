using BookIt.Application.Abstractions;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Mapping;
using BookIt.Domain.Entities;

namespace BookIt.Application.Services;

public class ResourceService(IResourceRepository repository) : IResourceService
{
    public async Task<List<ResourceDto>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var resources = await repository.GetAllAsync(includeInactive, cancellationToken);
        return resources.Select(r => r.ToDto()).ToList();
    }

    public async Task<Result<ResourceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var resource = await repository.GetByIdAsync(id, cancellationToken);
        return resource is null
            ? Result<ResourceDto>.Failure("Resource not found.")
            : Result<ResourceDto>.Success(resource.ToDto());
    }

    public async Task<ResourceDto> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken = default)
    {
        var resource = Resource.Create(request.Name, request.Type, request.Capacity, request.Description);
        await repository.AddAsync(resource, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return resource.ToDto();
    }

    public async Task<Result<ResourceDto>> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken = default)
    {
        var resource = await repository.GetByIdAsync(id, cancellationToken);
        if (resource is null)
        {
            return Result<ResourceDto>.Failure("Resource not found.");
        }

        resource.Update(request.Name, request.Description, request.Capacity);
        await repository.SaveChangesAsync(cancellationToken);
        return Result<ResourceDto>.Success(resource.ToDto());
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var resource = await repository.GetByIdAsync(id, cancellationToken);
        if (resource is null)
        {
            return Result.Failure("Resource not found.");
        }

        resource.Deactivate();
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
