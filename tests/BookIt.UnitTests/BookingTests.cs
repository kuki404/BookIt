using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BookIt.UnitTests;

public class BookingTests
{
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // A fixed instant driven by FakeTimeProvider rather than DateTime.UtcNow — "now" is exactly
    // controlled here, so there is no ambient clock drift between computing a start time and
    // Booking.Create's own past-start check (the reason the old `-1 minute` fudge factor existed).
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private static DateTime Now => Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public void Create_WithEndBeforeStart_Throws()
    {
        var start = Now.AddHours(2);
        var end = start.AddHours(-1);

        Should.Throw<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null, Now));
    }

    [Fact]
    public void Create_WithStartInThePast_Throws()
    {
        var start = Now.AddHours(-1);
        var end = start.AddHours(1);

        Should.Throw<DomainException>(() => Booking.Create(ResourceId, UserId, start, end, null, Now));
    }

    [Fact]
    public void Create_WithStartExactlyNow_Succeeds()
    {
        // Proves the fudge factor is no longer needed: start == now, compared against the exact
        // same instant (no ambient DateTime.UtcNow drift), succeeds rather than throwing.
        var booking = Booking.Create(ResourceId, UserId, Now, Now.AddHours(1), null, Now);

        booking.Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public void Create_WithValidRange_StartsAsPending()
    {
        var start = Now.AddHours(1);
        var booking = Booking.Create(ResourceId, UserId, start, start.AddHours(1), "Team sync", Now);

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
        var start = Now.AddHours(1);
        return Booking.Create(ResourceId, UserId, start, start.AddHours(1), null, Now);
    }
}
