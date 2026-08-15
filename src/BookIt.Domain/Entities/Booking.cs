using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;

namespace BookIt.Domain.Entities;

/// <summary>
/// Rich domain entity: state transitions are only valid through the methods below, never by
/// setting <see cref="Status"/> directly, so an invalid transition (e.g. completing a booking
/// that was never checked in) fails in the domain layer rather than deep inside a controller.
/// </summary>
public class Booking
{
    public Guid Id { get; private set; }
    public Guid ResourceId { get; private set; }
    public Resource? Resource { get; private set; }
    public Guid UserId { get; private set; }
    public string ReferenceCode { get; private set; } = string.Empty;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public BookingStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReminderSentAtUtc { get; private set; }

    /// <summary>EF Core concurrency token — protects against two users confirming/cancelling the same booking at once.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private Booking()
    {
        // EF Core materialization constructor.
    }

    public static Booking Create(Guid resourceId, Guid userId, DateTime startUtc, DateTime endUtc, string? notes)
    {
        if (startUtc >= endUtc)
        {
            throw new DomainException("Booking start time must be before the end time.");
        }

        if (startUtc < DateTime.UtcNow.AddMinutes(-1))
        {
            throw new DomainException("Booking start time cannot be in the past.");
        }

        return new Booking
        {
            Id = Guid.NewGuid(),
            ResourceId = resourceId,
            UserId = userId,
            ReferenceCode = GenerateReferenceCode(),
            StartUtc = startUtc,
            EndUtc = endUtc,
            Notes = notes?.Trim(),
            Status = BookingStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Confirm()
    {
        if (Status != BookingStatus.Pending)
        {
            throw new DomainException($"Cannot confirm a booking in '{Status}' status.");
        }

        Status = BookingStatus.Confirmed;
    }

    public void CheckIn()
    {
        if (Status != BookingStatus.Confirmed)
        {
            throw new DomainException($"Cannot check in a booking in '{Status}' status.");
        }

        Status = BookingStatus.CheckedIn;
    }

    public void Complete()
    {
        if (Status != BookingStatus.CheckedIn)
        {
            throw new DomainException($"Cannot complete a booking in '{Status}' status.");
        }

        Status = BookingStatus.Completed;
    }

    public void Cancel(string? reason)
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
        {
            throw new DomainException($"Cannot cancel a booking in '{Status}' status.");
        }

        Status = BookingStatus.Cancelled;
        CancellationReason = reason?.Trim();
    }

    public void MarkReminderSent() => ReminderSentAtUtc = DateTime.UtcNow;

    private static string GenerateReferenceCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I to avoid ambiguity
        Span<char> buffer = stackalloc char[6];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return $"BK-{new string(buffer)}";
    }
}
