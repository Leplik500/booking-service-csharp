using BookingService.Entities;

namespace BookingService.Infrastructure.Messaging.Contracts;

public class BookingStatusChangedEvent
{
    public long BookingId { get; init; }
    public BookingStatus OldStatus { get; init; }
    public BookingStatus NewStatus { get; init; }
    public DateTimeOffset ChangedAt { get; init; }

    public BookingStatusChangedEvent(
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus,
        DateTimeOffset changedAt
    )
    {
        BookingId = bookingId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ChangedAt = changedAt;
    }
}
