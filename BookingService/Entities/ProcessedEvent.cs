namespace BookingService.Entities;

public record ProcessedEvent(Guid EventId, DateTimeOffset ProcessedAt)
{
    public static ProcessedEvent Create(Guid eventId)
    {
        return new ProcessedEvent(eventId, DateTimeOffset.UtcNow);
    }
}
