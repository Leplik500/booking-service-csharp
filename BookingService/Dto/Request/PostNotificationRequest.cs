using BookingService.Entities;

namespace BookingService.Dto.Request;

public record PostNotificationRequest(
    long BookingId,
    BookingStatus OldStatus,
    BookingStatus NewStatus
);
