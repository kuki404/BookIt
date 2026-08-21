using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using Shouldly;

namespace BookIt.UnitTests;

public class ResourceTests
{
    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Should.Throw<ArgumentException>(() => Resource.Create("  ", ResourceType.Room, 1));
    }

    [Fact]
    public void Create_WithZeroCapacity_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Resource.Create("Room A", ResourceType.Room, 0));
    }

    [Fact]
    public void Create_IsActiveByDefault()
    {
        var resource = Resource.Create("Room A", ResourceType.Room, 4);

        resource.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var resource = Resource.Create("Room A", ResourceType.Room, 4);

        resource.Deactivate();

        resource.IsActive.ShouldBeFalse();
    }
}
