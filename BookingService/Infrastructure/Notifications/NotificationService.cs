using BookingService.Dto.Request;
using BookingService.Entities;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BookingService.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly HttpClient _httpClient;

    public NotificationService(ILogger<NotificationService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task NotifyStatusChangedAsync(
        long bookingId,
        BookingStatus oldStatus,
        BookingStatus newStatus
    )
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "/api/notifications",
                new PostNotificationRequest(bookingId, oldStatus, newStatus)
            );

            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or TimeoutRejectedException
                        or BrokenCircuitException
            )
        {
            _logger.LogWarning(
                exception,
                "Уведомление не было доставлено: oldStatus={OldStatus}, newStatus={NewStatus}, bookingId={BookingId}",
                oldStatus,
                newStatus,
                bookingId
            );
        }
    }
}
