using System.ComponentModel.DataAnnotations;
using BookIt.Domain.Enums;

namespace BookIt.Application.Dtos;

public record ResourceDto(
    Guid Id,
    string Name,
    string? Description,
    ResourceType Type,
    string TypeDisplay,
    int Capacity,
    bool IsActive);

public record CreateResourceRequest(
    [Required, MaxLength(200)] string Name,
    ResourceType Type,
    [Range(1, 1000)] int Capacity,
    [MaxLength(1000)] string? Description);

public record UpdateResourceRequest(
    [Required, MaxLength(200)] string Name,
    [Range(1, 1000)] int Capacity,
    [MaxLength(1000)] string? Description);
