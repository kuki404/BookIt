using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;
using Shouldly;

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

        Should.Throw<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null));
    }

    [Fact]
    public void Create_WithStartInThePast_Throws()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        var end = start.AddHours(1);

        Should.Throw<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null));
    }

    [Fact]
    public void Create_WithValidRange_StartsAsPending()
    {
        var start = DateTime.UtcNow.AddHours(1);
        var booking = Booking.Create(ResourceId, UserId, start, start.AddHours(1), "Team sync");

        booking.Status.ShouldBe(BookingStatus.Pending);
        booking.ReferenceCode.ShouldStartWith("BK-");
    }

    [Fact]
    public void Confirm_ThenCheckIn_ThenComplete_FollowsHappyPath()
    {
        var booking = CreatePendingBooking();

        booking.Confirm();
        booking.Status.ShouldBe(BookingStatus.Confirmed);

        booking.CheckIn();
        booking.Status.ShouldBe(BookingStatus.CheckedIn);

        booking.Complete();
        booking.Status.ShouldBe(BookingStatus.Completed);
    }

    [Fact]
    public void CheckIn_WithoutConfirming_Throws()
    {
        var booking = CreatePendingBooking();

        Should.Throw<DomainException>(booking.CheckIn);
    }

    [Fact]
    public void Complete_WithoutCheckIn_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Confirm();

        Should.Throw<DomainException>(booking.Complete);
    }

    [Fact]
    public void Cancel_APendingBooking_Succeeds()
    {
        var booking = CreatePendingBooking();

        booking.Cancel("changed my mind");

        booking.Status.ShouldBe(BookingStatus.Cancelled);
        booking.CancellationReason.ShouldBe("changed my mind");
    }

    [Fact]
    public void Cancel_AnAlreadyCompletedBooking_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Confirm();
        booking.CheckIn();
        booking.Complete();

        Should.Throw<DomainException>(() => booking.Cancel(null));
    }

    [Fact]
    public void Cancel_AnAlreadyCancelledBooking_Throws()
    {
        var booking = CreatePendingBooking();
        booking.Cancel(null);

        Should.Throw<DomainException>(() => booking.Cancel(null));
    }

    private static Booking CreatePendingBooking()
    {
        var start = DateTime.UtcNow.AddHours(1);
        return Booking.Create(ResourceId, UserId, start, start.AddHours(1), null);
    }
}
