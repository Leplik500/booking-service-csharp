namespace BookingService.Configuration;

/// <summary>
/// Настройки сервиса уведомлений
/// </summary>
public class NotificationServiceSettings
{
    /// <summary>
    /// Базовый URL API сервиса уведомлений
    /// </summary>
    public required string BaseUrl { get; set; }

    public Retry Retry { get; set; }
}

public class Retry
{
    /// <summary>
    /// Максимальное количество повторных попыток отправить уведомление
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    /// Интервал между повторными попытками отправки уведомления
    /// </summary>
    public int DelaySeconds { get; set; }
}
