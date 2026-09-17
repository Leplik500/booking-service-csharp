using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using RabbitMQ.Client.Exceptions;

namespace BookingService.Services;

public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public StuckCancellationsJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var bookingRepository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
            var dateTimeProvider =
                scope.ServiceProvider.GetRequiredService<ICurrentDateTimeProvider>();

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<StuckCancellationsJob>>();

            var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

            var cancellingBookings = await bookingRepository.FindStuckCancellationsAsync(
                dateTimeProvider.UtcNow() - TimeSpan.FromMinutes(5)
            );

            logger.LogWarning("Найдено {count} зависших отмен", cancellingBookings.Count);
            foreach (var booking in cancellingBookings)
            {
                if (booking.CatalogRequestId == null)
                    continue;

                try
                {
                    await publisher.PublishCancelBookingJob(
                        new CancelBookingJobByRequestIdRequest
                        {
                            EventId = Guid.NewGuid(),
                            RequestId = (Guid)booking.CatalogRequestId,
                        }
                    );

                    logger.LogWarning(
                        "Бронирование {requestId} было отменено заново",
                        booking.CatalogRequestId
                    );
                }
                catch (BrokerUnreachableException e)
                {
                    logger.LogError(e, "Произошла ошибка при повторной отмене зависшего запроса");
                }
            }
        }
    }
}
