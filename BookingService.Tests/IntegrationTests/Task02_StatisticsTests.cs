using BookingService.Entities;
using BookingService.Tests.Fixtures;
using FluentAssertions;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 02 — Endpoint статистики.
///
/// Проверяют:
/// 1. Корректный подсчёт общего числа бронирований
/// 2. Корректную разбивку по статусам
/// 3. Топ-5 ресурсов по количеству бронирований (правильный порядок + ограничение)
/// 4. Работу при пустой БД
///
/// Все агрегации должны выполняться на стороне PostgreSQL, а не в памяти.
/// Тесты используют реальную БД — это гарантирует корректность SQL-запросов.
/// </summary>
public class Task02_StatisticsTests : IntegrationTestBase
{
    [Fact]
    public async Task GetStatistics_WithNoBookings_ReturnsZeroTotals()
    {
        // Act
        var stats = await BookingService.GetStatistics();

        // Assert
        stats.TotalCount.Should().Be(0);
        stats.ByStatus.Should().BeEmpty();
        stats.TopResources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectTotalCount()
    {
        // Arrange: создаём 3 бронирования
        await CreateBookingAsync(1, 1);
        await CreateBookingAsync(2, 2);
        await CreateBookingAsync(3, 3);

        // Act
        var stats = await BookingService.GetStatistics();

        // Assert
        stats
            .TotalCount.Should()
            .Be(3, "Должно возвращаться корректное общее число всех бронирований");
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectStatusBreakdown()
    {
        // Arrange: создаём бронирования в разных статусах
        // 3 в AwaitConfirmation
        var (_, req1) = await CreateBookingAsync(1, 1);
        var (_, req2) = await CreateBookingAsync(2, 1);
        await CreateBookingAsync(3, 1);
        // 1 в Confirmed
        await ConfirmBookingAsync(req1);
        // 1 в Cancelled (через deny)
        await BookingService.HandleBookingJobDenied(req2);

        // Act
        var stats = await BookingService.GetStatistics();

        // Assert
        stats.TotalCount.Should().Be(3);

        var awaitCount = stats.ByStatus.FirstOrDefault(s =>
            s.Status == BookingStatus.AwaitConfirmation
        );

        var confirmedCount = stats.ByStatus.FirstOrDefault(s =>
            s.Status == BookingStatus.Confirmed
        );

        var cancelledCount = stats.ByStatus.FirstOrDefault(s =>
            s.Status == BookingStatus.Cancelled
        );

        awaitCount.Should().NotBeNull();
        awaitCount!.Count.Should().Be(1, "Одно бронирование должно оставаться в AwaitConfirmation");

        confirmedCount.Should().NotBeNull();
        confirmedCount!.Count.Should().Be(1, "Одно бронирование подтверждено");

        cancelledCount.Should().NotBeNull();
        cancelledCount!.Count.Should().Be(1, "Одно бронирование отклонено Catalog Service");
    }

    [Fact]
    public async Task GetStatistics_ReturnsTopResourcesSortedByBookingCountDescending()
    {
        // Arrange: ресурс 10 — 3 бронирования, ресурс 20 — 2, ресурс 30 — 1
        await CreateBookingAsync(1, 10);
        await CreateBookingAsync(2, 10);
        await CreateBookingAsync(3, 10);
        await CreateBookingAsync(4, 20);
        await CreateBookingAsync(5, 20);
        await CreateBookingAsync(6, 30);

        // Act
        var stats = await BookingService.GetStatistics();

        // Assert
        stats.TopResources.Should().HaveCountGreaterThanOrEqualTo(3);
        stats
            .TopResources.First()
            .ResourceId.Should()
            .Be(10, "Ресурс с наибольшим числом бронирований должен быть первым");

        stats.TopResources.First().BookingCount.Should().Be(3);
        stats
            .TopResources.Should()
            .BeInDescendingOrder(
                r => r.BookingCount,
                "Топ ресурсов должен быть отсортирован по убыванию числа бронирований"
            );
    }

    [Fact]
    public async Task GetStatistics_TopResources_ReturnsMaxFiveEntries()
    {
        // Arrange: создаём бронирования для 7 разных ресурсов
        for (var resourceId = 1; resourceId <= 7; resourceId++)
            await CreateBookingAsync(resourceId, resourceId, 5 + resourceId);

        // Act
        var stats = await BookingService.GetStatistics();

        // Assert
        stats
            .TopResources.Should()
            .HaveCountLessOrEqualTo(5, "Топ ресурсов должен содержать не более 5 позиций");
    }
}
