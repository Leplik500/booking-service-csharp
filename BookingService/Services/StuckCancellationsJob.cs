using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Services;

public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckCancellationsJob> _logger;

    public StuckCancellationsJob(
        IServiceScopeFactory scopeFactory,
        ILogger<StuckCancellationsJob> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(1);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var bookingRepository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
            var dateTimeProvider =
                scope.ServiceProvider.GetRequiredService<ICurrentDateTimeProvider>();

            var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

            var stuckTimeout = dateTimeProvider.UtcNow() - TimeSpan.FromMinutes(5);
            var cancellingBookings = await bookingRepository.FindStuckCancellationsAsync(
                stuckTimeout,
                stoppingToken
            );

            var cancellingBookingsCount = cancellingBookings.Count;
            if (cancellingBookingsCount > 0)
                _logger.LogInformation("Найдено {count} зависших отмен", cancellingBookingsCount);

            foreach (var booking in cancellingBookings)
            {
                try
                {
                    if (booking.CatalogRequestId == null)
                        continue;

                    await publisher.PublishCancelBookingJob(
                        new CancelBookingJobByRequestIdRequest
                        {
                            EventId = Guid.NewGuid(),
                            RequestId = (Guid)booking.CatalogRequestId,
                        },
                        stoppingToken
                    );

                    _logger.LogInformation(
                        "Бронирование {requestId} было отменено заново",
                        booking.CatalogRequestId
                    );
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation(
                        "Повторный запрос отмены бронирования {requestId} был отменён",
                        booking.CatalogRequestId
                    );
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Произошла ошибка: {Message}", e.Message);
                }
            }
        }
    }
}
