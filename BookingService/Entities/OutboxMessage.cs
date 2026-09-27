using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BookingService.Entities;

public class OutboxMessage
{
    public static OutboxMessage Create<T>(T message)
    {
        return new OutboxMessage
        {
            MessageType = typeof(T).FullName ?? throw new InvalidOperationException(),
            Payload = JsonSerializer.SerializeToDocument(message),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// ID сообщения
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Тип сообщения
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Сериализованное сообщение
    /// </summary>
    public required JsonDocument Payload { get; init; }

    /// <summary>
    /// Время создания
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Время успешной публикации
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Количество повторных отправок
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Время, когда сообщение исчерпало доступные попытки отправки
    /// </summary>
    public DateTimeOffset? FailedAt { get; set; }
}
