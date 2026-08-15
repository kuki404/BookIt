using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;

namespace BookIt.UnitTests;

public class BookingTests
{
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_WithEndBeforeStart_Throws()
    {
        var start = DateTime.UtcNow.AddHours(2);
        var end = start.AddHours(-1);

        Assert.Throws<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null));
    }

    [Fact]
    public void Create_WithStartInThePast_Throws()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        var end = start.AddHours(1);

        Assert.Throws<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null));
    }

    [Fact]
    public void Create_WithValidRange_StartsAsPending()
    {
        var start = DateTime.UtcNow.AddHours(1);
        var booking = Booking.Create(ResourceId, UserId, start, start.AddHours(1), "Team sync");

        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.StartsWith("BK-", booking.ReferenceCode);
    }

    [Fact]
    public void Confirm_ThenCheckIn_ThenComplete_FollowsHappyPath()
    {
        var booking = CreatePendingBooking();

        booking.Confirm();
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        booking.CheckIn();
        Assert.Equal(BookingStatus.CheckedIn, booking.Status);

        booking.Complete();
        Assert.Equal(BookingStatus.Completed, booking.Status);
    }

    [Fact]
    public void CheckIn_WithoutConfirming_Throws()
    {
        var booking = CreatePendingBooking();

        Assert.Throws<DomainException>(booking.CheckIn);
    }

    [Fact]
    public void Complete_WithoutCheckIn_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Confirm();

        Assert.Throws<DomainException>(booking.Complete);
    }

    [Fact]
    public void Cancel_APendingBooking_Succeeds()
    {
        var booking = CreatePendingBooking();

        booking.Cancel("changed my mind");

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("changed my mind", booking.CancellationReason);
    }

    [Fact]
    public void Cancel_AnAlreadyCompletedBooking_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Confirm();
        booking.CheckIn();
        booking.Complete();

        Assert.Throws<DomainException>(() => booking.Cancel(null));
    }

    [Fact]
    public void Cancel_AnAlreadyCancelledBooking_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Cancel(null);

        Assert.Throws<DomainException>(() => booking.Cancel(null));
    }

    private static Booking CreatePendingBooking()
    {
        var start = DateTime.UtcNow.AddHours(1);
        return Booking.Create(ResourceId, UserId, start, start.AddHours(1), null);
    }
}
