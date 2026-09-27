using System.Text.Json;
using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using Rebus.Bus;

namespace BookingService.Services;

public class OutboxProcessorJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(3);
    private const int _maxTries = 3;

    public OutboxProcessorJob(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorJob> logger)
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

                var bus = scope.ServiceProvider.GetRequiredService<IBus>();

                var messages = await bookingRepository.GetUnprocessedMessagesAsync(stoppingToken);
                foreach (var message in messages)
                {
                    try
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        var messageType = Type.GetType(message.MessageType);
                        if (messageType != null)
                        {
                            var payload = message.Payload.Deserialize(messageType);

                            await bus.Publish(payload);
                        }
                        else
                        {
                            message.FailedAt = dateTimeProvider.UtcNow();
                            _logger.LogWarning(
                                "Не удалось найти тип {MessageType}",
                                message.MessageType
                            );
                        }

                        message.ProcessedAt = dateTimeProvider.UtcNow();

                        _logger.LogInformation(
                            "Сообщение {Id} было отправлено в RabbitMQ",
                            message.Id
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Отправка сообщения {Id} была отменена", message.Id);

                        return;
                    }
                    catch (Exception e)
                    {
                        if (message.RetryCount >= _maxTries)
                        {
                            message.FailedAt = dateTimeProvider.UtcNow();
                            _logger.LogError(
                                e,
                                "Сообщение {Id} не отправлено: {Message}. Было совершено {RetryCount} попытки",
                                message.Id,
                                e.Message,
                                message.RetryCount
                            );
                        }
                        else
                        {
                            _logger.LogWarning(
                                e,
                                "Сообщение {Id} не отправлено: {Message}. Было совершено {RetryCount} попытки",
                                message.Id,
                                e.Message,
                                message.RetryCount
                            );

                            message.RetryCount++;
                        }
                    }
                    finally
                    {
                        await bookingRepository.SaveAsync();
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
