using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;

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
            var bookingService = scope.ServiceProvider.GetRequiredService<BookingService>();
            var dateTimeProvider =
                scope.ServiceProvider.GetRequiredService<CurrentDateTimeProvider>();

            var publisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

            var cancellingBookings = await GetAllCancellingBookings(bookingService);
            foreach (
                var booking in cancellingBookings.Where(booking =>
                    dateTimeProvider.UtcNow() - booking.CreatedAt > TimeSpan.FromMinutes(5)
                )
            )
            {
                await publisher.PublishCancelBookingJob(
                    new CancelBookingJobByRequestIdRequest
                    {
                        EventId = Guid.Empty,
                        RequestId = (Guid)booking.CatalogRequestId!,
                    }
                );
            }
        }
    }

    private static async Task<List<Booking>> GetAllCancellingBookings(BookingService bookingService)
    {
        var allBookings = new List<Booking>();
        var page = 1;
        const int pageSize = 100;
        List<Booking> current;

        do
        {
            current = await bookingService.GetByFilter(
                null,
                null,
                BookingStatus.CancellationPending,
                page,
                pageSize
            );

            allBookings.AddRange(current);
            page++;
        } while (current.Count == pageSize);

        return allBookings;
    }
}
