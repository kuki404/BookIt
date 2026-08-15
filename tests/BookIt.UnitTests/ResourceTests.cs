using BookIt.Domain.Entities;
using BookIt.Domain.Enums;

namespace BookIt.UnitTests;

public class ResourceTests
{
    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Resource.Create("  ", ResourceType.Room, 1));
    }

    [Fact]
    public void Create_WithZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Resource.Create("Room A", ResourceType.Room, 0));
    }

    [Fact]
    public void Create_IsActiveByDefault()
    {
        var resource = Resource.Create("Room A", ResourceType.Room, 4);

        Assert.True(resource.IsActive);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var resource = Resource.Create("Room A", ResourceType.Room, 4);

        resource.Deactivate();

        Assert.False(resource.IsActive);
    }
}
