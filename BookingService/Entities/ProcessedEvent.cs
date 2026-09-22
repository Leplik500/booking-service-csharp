namespace BookingService.Entities;

public record ProcessedEvent(Guid EventId, DateTimeOffset ProcessedAt)
{
    public static ProcessedEvent Create(Guid EventId)
    {
        return new ProcessedEvent(EventId, DateTimeOffset.UtcNow);
    }
}
