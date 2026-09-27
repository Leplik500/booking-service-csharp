using System.Text.Json;
using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using Rebus.Bus;

namespace BookingService.Services;

public class OutboxProcessorJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
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

                var batches = bookingRepository.GetUnprocessedMessagesAsync(stoppingToken);

                await foreach (var batch in batches)
                {
                    foreach (var message in batch)
                    {
                        try
                        {
                            stoppingToken.ThrowIfCancellationRequested();

                            var messageType = Type.GetType(message.MessageType);
                            message.Payload.Deserialize(
                                messageType ?? throw new InvalidOperationException()
                            );

                            await bus.Send(message);
                            message.ProcessedAt = dateTimeProvider.UtcNow();

                            _logger.LogInformation(
                                "Сообщение {Id} было отправлено в RabbitMQ",
                                message.Id
                            );
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation(
                                "Отправка сообщения {Id} была отменена",
                                message.Id
                            );

                            return;
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning(e, "Сообщение не отправлено: {Message}", e.Message);
                            message.RetryCount++;
                            if (message.RetryCount >= _maxTries)
                                message.FailedAt = dateTimeProvider.UtcNow();
                        }
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
