namespace BookingService.Dto.Request;

public record ProcessedEvent(Guid EventId, DateTimeOffset ProcessedAt);
