using BookingService.Configuration;
using BookingService.Dto.Response;
using BookingService.Entities;
using BookingService.Exceptions;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace BookingService.Services;

/// <summary>
/// Сервис для работы с бронированиями.
/// Объединяет CRUD операции, бизнес-логику и обработку событий от Catalog Service.
/// </summary>
public class BookingService
{
    private readonly BookingRepository _repository;
    private readonly BookingEventPublisher _publisher;
    private readonly ICurrentDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BookingService> _logger;

    // [Task 09] Опциональная зависимость — существующие тесты создают сервис без неё
    private readonly INotificationService? _notificationService;

    // [Task 10] Опциональная зависимость — существующие тесты создают сервис без неё
    private readonly IMemoryCache? _cache;

    public BookingService(
        BookingRepository repository,
        BookingEventPublisher publisher,
        ICurrentDateTimeProvider dateTimeProvider,
        ILogger<BookingService> logger,
        INotificationService? notificationService = null,
        IMemoryCache? cache = null
    )
    {
        _repository = repository;
        _publisher = publisher;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _notificationService = notificationService;
        _cache = cache;
    }

    // === КОМАНДЫ (Use Cases) ===

    /// <summary>
    /// Создать новое бронирование.
    /// Отправляет асинхронную команду в Catalog Service для резервирования ресурса.
    /// </summary>
    /// <returns>ID созданного бронирования</returns>
    public async Task<long> CreateBooking(
        long userId,
        long resourceId,
        DateOnly bookedFrom,
        DateOnly bookedTo
    )
    {
        var now = _dateTimeProvider.UtcNow();
        var booking = Booking.Create(userId, resourceId, bookedFrom, bookedTo, now);

        var requestId = Guid.NewGuid();
        booking.SetCatalogRequestId(requestId);

        await _repository.SaveAsync(booking);

        var command = new CreateBookingJobRequest
        {
            EventId = Guid.NewGuid(),
            RequestId = requestId,
            ResourceId = booking.ResourceId,
            StartDate = booking.BookedFrom,
            EndDate = booking.BookedTo,
        };

        await _publisher.PublishCreateBookingJob(command);

        _logger.LogInformation(
            "Создано бронирование с ID: {Id}, requestId: {RequestId}",
            booking.Id,
            requestId
        );

        return booking.Id;
    }

    /// <summary>
    /// Отменить бронирование.
    /// Для подтверждённых бронирований переходит в CancellationPending (Задача 01).
    /// </summary>
    public async Task CancelBooking(long id)
    {
        var booking =
            await _repository.FindByIdAsync(id)
            ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");

        var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);

        var oldStatus = booking.Status;
        booking.Cancel(currentDate);

        await _repository.SaveAsync(booking);

        var newStatus = booking.Status;
        await _publisher.PublishStatusChanged(
            new BookingStatusChangedEvent(
                booking.Id,
                oldStatus,
                newStatus,
                _dateTimeProvider.UtcNow()
            )
        );

        if (booking.CatalogRequestId is not null)
        {
            var command = new CancelBookingJobByRequestIdRequest
            {
                EventId = Guid.NewGuid(),
                RequestId = booking.CatalogRequestId.Value,
            };

            await _publisher.PublishCancelBookingJob(command);
        }

        _logger.LogInformation(
            "Инициирована отмена бронирования с ID: {Id}, новый статус: {Status}",
            id,
            booking.Status
        );
    }

    // === ЗАПРОСЫ (Queries) ===

    /// <summary>Получить бронирование по ID</summary>
    public async Task<Booking> GetById(long id)
    {
        return await _repository.FindByIdAsync(id)
            ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");
    }

    /// <summary>Получить бронирования по фильтрам с пагинацией</summary>
    public async Task<List<Booking>> GetByFilter(
        long? userId,
        long? resourceId,
        BookingStatus? status,
        int pageNumber,
        int pageSize
    )
    {
        return await _repository.FindByFilterAsync(
            userId,
            resourceId,
            status,
            pageNumber,
            pageSize
        );
    }

    /// <summary>Получить только статус бронирования по ID</summary>
    public async Task<BookingStatus?> GetStatusById(long id)
    {
        return await _repository.FindStatusByIdAsync(id);
    }

    public async Task<StatisticsResponse> GetStatistics()
    {
        return await _repository.GetStatisticsAsync();
    }

    // === EVENT HANDLERS (Обработка асинхронных событий от Catalog Service) ===

    /// <summary>
    /// Обработать событие подтверждения booking job от Catalog Service.
    /// Переводит бронирование в статус Confirmed.
    /// </summary>
    public async Task HandleBookingJobConfirmed(Guid requestId, Guid eventId)
    {
        _logger.LogInformation(
            "Получено событие BookingJobConfirmed: requestId={RequestId}",
            requestId
        );

        if (await _repository.IsEventProcessedAsync(eventId))
        {
            _logger.LogWarning(
                "Событие BookingJobConfirmed: requestId={RequestId} уже обработано",
                requestId
            );

            return;
        }

        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning(
                "Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.",
                requestId
            );

            return;
        }

        if (booking.Status != BookingStatus.AwaitConfirmation)
        {
            _logger.LogWarning(
                "Бронирование id={Id} в статусе {Status} — BookingJobConfirmed проигнорировано",
                booking.Id,
                booking.Status
            );

            return;
        }

        _logger.LogInformation(
            "Найдено бронирование: id={Id}, статус={Status}. Подтверждаем...",
            booking.Id,
            booking.Status
        );

        _repository.TrackProcessedEvent(eventId);
        const int maxTries = 3;
        for (var i = 0; i < maxTries; i++)
            try
            {
                var oldStatus = booking.Status;
                booking.Confirm();

                await _repository.SaveAsync(booking);

                var newStatus = booking.Status;
                await _publisher.PublishStatusChanged(
                    new BookingStatusChangedEvent(
                        booking.Id,
                        oldStatus,
                        newStatus,
                        _dateTimeProvider.UtcNow()
                    )
                );

                _logger.LogInformation(
                    "Бронирование успешно подтверждено: id={Id}, новый статус={Status}",
                    booking.Id,
                    booking.Status
                );

                break;
            }
            catch (DbUpdateConcurrencyException e)
            {
                if (i == maxTries - 1)
                {
                    _logger.LogError(
                        e,
                        "Произошла ошибка при подтверждении бронирования: {Message}",
                        e.Message
                    );

                    throw;
                }

                await e.Entries.FirstOrDefault(x => x.Entity is Booking)?.ReloadAsync()!;
                if (booking.Status == BookingStatus.AwaitConfirmation)
                    continue;

                _logger.LogWarning(
                    "Бронирование не подтвеждено: id={Id}, статус=CancellationPending",
                    booking.Id
                );

                return;
            }
            catch (DbUpdateException e)
                when (e.InnerException is PostgresException { SqlState: "23505" })
            {
                _logger.LogWarning(
                    "Событие BookingJobConfirmed: eventId={EventId} уже обработано",
                    eventId
                );

                return;
            }
    }

    /// <summary>
    /// Обработать событие отклонения booking job от Catalog Service.
    /// Отменяет бронирование.
    /// </summary>
    public async Task HandleBookingJobDenied(Guid requestId, Guid eventId)
    {
        _logger.LogInformation(
            "Получено событие BookingJobDenied: requestId={RequestId}",
            requestId
        );

        if (await _repository.IsEventProcessedAsync(eventId))
        {
            _logger.LogWarning(
                "Событие BookingJobDenied: requestId={RequestId} уже обработано",
                requestId
            );

            return;
        }

        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning(
                "Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.",
                requestId
            );

            return;
        }

        if (
            booking.Status != BookingStatus.AwaitConfirmation
            && booking.Status != BookingStatus.Confirmed
        )
        {
            _logger.LogWarning(
                "Бронирование id={Id} в статусе {Status} — BookingJobDenied проигнорировано",
                booking.Id,
                booking.Status
            );

            return;
        }

        _logger.LogInformation(
            "Найдено бронирование: id={Id}, статус={Status}. Отменяем...",
            booking.Id,
            booking.Status
        );

        var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);
        var oldStatus = booking.Status;

        booking.Cancel(currentDate);
        _repository.TrackProcessedEvent(eventId);
        try
        {
            await _repository.SaveAsync(booking);
            var newStatus = booking.Status;
            await _publisher.PublishStatusChanged(
                new BookingStatusChangedEvent(
                    booking.Id,
                    oldStatus,
                    newStatus,
                    _dateTimeProvider.UtcNow()
                )
            );
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            _logger.LogWarning(
                "Событие BookingJobDenied: eventId={EventId} уже обработано",
                eventId
            );

            return;
        }

        _logger.LogInformation(
            "Бронирование успешно отменено: id={Id}, новый статус={Status}",
            booking.Id,
            booking.Status
        );
    }

    /// <summary>
    /// Обработать ошибку при отмене
    /// </summary>
    /// <param name="requestId"></param>
    /// <param name="eventId"></param>
    public async Task HandleCancellationError(Guid requestId, Guid eventId)
    {
        _logger.LogWarning(
            "Произошла ошибка отмены бронирования: requestId={RequestId}",
            requestId
        );

        if (await _repository.IsEventProcessedAsync(eventId))
        {
            _logger.LogWarning(
                "Событие CancellationError: requestId={RequestId} уже обработано",
                requestId
            );

            return;
        }

        var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
        if (booking is null)
        {
            _logger.LogWarning(
                "Бронирование не найдено по requestId: {RequestId}. Ошибка проигнорирована.",
                requestId
            );

            return;
        }

        _logger.LogInformation(
            "Найдено бронирование: id={Id}, статус={Status}. Откатываем...",
            booking.Id,
            booking.Status
        );

        if (booking.Status != BookingStatus.CancellationPending)
        {
            _logger.LogWarning(
                "Откат отмены бронирования не был произведён из-за недопустимого статуса: статус={Status}",
                booking.Status
            );

            return;
        }

        var oldStatus = booking.Status;
        booking.RollbackCancellation();

        _repository.TrackProcessedEvent(eventId);
        try
        {
            await _repository.SaveAsync(booking);

            var newStatus = booking.Status;
            await _publisher.PublishStatusChanged(
                new BookingStatusChangedEvent(
                    booking.Id,
                    oldStatus,
                    newStatus,
                    _dateTimeProvider.UtcNow()
                )
            );
        }
        catch (DbUpdateException e)
            when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            _logger.LogWarning(
                "Событие BookingJobDenied: eventId={EventId} уже обработано",
                eventId
            );

            return;
        }

        _logger.LogInformation(
            "Был произведён откат отмены бронирования: id={Id}, новый статус={Status}",
            booking.Id,
            booking.Status
        );
    }

    /// <summary>Вернуть историю изменений статусов для указанного бронирования</summary>
    public async Task<List<BookingStatusHistory>> GetBookingHistory(
        long bookingId,
        CancellationToken cancellationToken
    )
    {
        await GetById(bookingId);
        return await _repository.GetBookingHistoryAsync(bookingId, cancellationToken);
    }
}
