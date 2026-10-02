using BookingService.Configuration;
using BookingService.Entities;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BookingService.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotificationServiceSettings _options;

    public NotificationService(ILogger<NotificationService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<NotificationServiceSettings> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task NotifyStatusChangedAsync(
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus
    )
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient(nameof(NotificationService));
            using var response = await httpClient.PostAsync(
                _options.BaseUrl,
                new StringContent(
                    $"Бронирование {bookingId} перешло из статуса {oldStatus} в {newStatus}"
                )
            );

            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            _logger.LogWarning(exception, "Уведомление не было доставлено после всех {MaxAttemtps} попыток", _options.MaxAttempts);
        }
    }
}
