using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Сервис для публикации сообщений в RabbitMQ через Rebus.
/// Rebus автоматически устанавливает заголовки и маршрутизирует сообщения.
/// </summary>
public class BookingEventTracker
{
    private readonly ILogger<BookingEventTracker> _logger;
    private readonly BookingRepository _bookingRepository;

    public BookingEventTracker(
        ILogger<BookingEventTracker> logger,
        BookingRepository bookingRepository
    )
    {
        _logger = logger;
        _bookingRepository = bookingRepository;
    }

    /// <summary>
    /// Публикует команду создания booking job в Catalog Service
    /// </summary>
    public async Task TrackCreateBookingJob(CreateBookingJobRequest request)
    {
        _logger.LogInformation(
            "Публикация команды CreateBookingJob: requestId={RequestId}, resourceId={ResourceId}, dates={Start} - {End}",
            request.RequestId,
            request.ResourceId,
            request.StartDate,
            request.EndDate
        );

        await _bookingRepository.TrackOutboxMessageAsync(request);

        _logger.LogInformation("Команда CreateBookingJob отправлена в RabbitMQ");
    }

    /// <summary>
    /// Публикует команду отмены booking job в Catalog Service
    /// </summary>
    public async Task TrackCancelBookingJob(CancelBookingJobByRequestIdRequest request)
    {
        _logger.LogInformation(
            "Публикация команды CancelBookingJob: requestId={RequestId}",
            request.RequestId
        );

        await _bookingRepository.TrackOutboxMessageAsync(request);

        _logger.LogInformation("Команда CancelBookingJob отправлена в RabbitMQ");
    }

    public async Task TrackStatusChanged(BookingStatusChangedEvent bookingStatusChangedEvent)
    {
        _logger.LogInformation(
            "Публикация события StatusChanged: BookingId={BookingId}, OldStatus={OldStatus}, NewStatus={NewStatus}, ChangedAt={ChangedAt}",
            bookingStatusChangedEvent.BookingId,
            bookingStatusChangedEvent.OldStatus,
            bookingStatusChangedEvent.NewStatus,
            bookingStatusChangedEvent.ChangedAt
        );

        await _bookingRepository.TrackOutboxMessageAsync(bookingStatusChangedEvent);

        _logger.LogInformation("Событие StatusChangedEvent отправлено в RabbitMQ");
    }
}
