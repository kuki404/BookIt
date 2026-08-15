using BookIt.Domain.Enums;

namespace BookIt.Domain.Entities;

public class Resource
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ResourceType Type { get; private set; }
    public int Capacity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public List<Booking> Bookings { get; private set; } = [];

    private Resource()
    {
        // EF Core materialization constructor.
    }

    public static Resource Create(string name, ResourceType type, int capacity, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Resource name is required.", nameof(name));
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        return new Resource
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim(),
            Type = type,
            Capacity = capacity,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Update(string name, string? description, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Resource name is required.", nameof(name));
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        Name = name.Trim();
        Description = description?.Trim();
        Capacity = capacity;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
