using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Services;

public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckCancellationsJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _stuckTimeout = TimeSpan.FromMinutes(5);

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
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bookingRepository =
                    scope.ServiceProvider.GetRequiredService<BookingRepository>();

                var dateTimeProvider =
                    scope.ServiceProvider.GetRequiredService<ICurrentDateTimeProvider>();

                var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

                var cancellingBookings = await bookingRepository.FindStuckCancellationsAsync(
                    dateTimeProvider.UtcNow() - _stuckTimeout,
                    stoppingToken
                );

                var cancellingBookingsCount = cancellingBookings.Count;
                if (cancellingBookingsCount > 0)
                    _logger.LogInformation(
                        "Найдено {count} зависших отмен",
                        cancellingBookingsCount
                    );

                foreach (var booking in cancellingBookings)
                {
                    try
                    {
                        if (booking.CatalogRequestId == null)
                            continue;

                        stoppingToken.ThrowIfCancellationRequested();
                        await publisher.PublishCancelBookingJob(
                            new CancelBookingJobByRequestIdRequest
                            {
                                EventId = Guid.NewGuid(),
                                RequestId = (Guid)booking.CatalogRequestId,
                            }
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

                        return;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Произошла ошибка: {Message}", e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Произошла ошибка: {Message}", e.Message);
            }
        }
    }
}
