using BookingService.Entities;

namespace BookingService.Infrastructure.Messaging.Contracts;

public class BookingStatusChangedEvent
{
    public long bookingId { get; init; }
    public BookingStatus oldStatus { get; init; }
    public BookingStatus newStatus { get; init; }
    public DateTimeOffset changedAt { get; init; }

    private BookingStatusChangedEvent(
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus,
        DateTimeOffset changedAt
    )
    {
        this.bookingId = bookingId;
        this.oldStatus = oldStatus;
        this.newStatus = newStatus;
        this.changedAt = changedAt;
    }

    public static BookingStatusChangedEvent Create(
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus
    )
    {
        return new BookingStatusChangedEvent(
            bookingId,
            oldStatus,
            newStatus,
            DateTimeOffset.UtcNow
        );
    }
}
