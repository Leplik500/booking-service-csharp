using BookingService.Dto.Response;
using BookingService.Entities;

namespace BookingService.Mappers;

/// <summary>
/// Маппер для преобразования Entity в DTO
/// </summary>
public class BookingMapper
{
    public BookingResponse ToResponse(Booking booking)
    {
        return new BookingResponse(
            booking.Id,
            booking.Status,
            booking.UserId,
            booking.ResourceId,
            booking.BookedFrom,
            booking.BookedTo,
            booking.CreatedAt
        );
    }

    public List<BookingResponse> ToResponseList(List<Booking> bookings)
    {
        return bookings.Select(ToResponse).ToList();
    }

    public BookingStatusHistoryResponse ToResponseHistory(BookingStatusHistory bookingStatusHistory)
    {
        return new BookingStatusHistoryResponse(
            bookingStatusHistory.Id,
            bookingStatusHistory.BookingId,
            bookingStatusHistory.StatusFrom,
            bookingStatusHistory.StatusTo,
            bookingStatusHistory.ChangedAt
        );
    }

    public List<BookingStatusHistoryResponse> ToResponseHistoryList(
        List<BookingStatusHistory> bookingStatusHistory
    )
    {
        return bookingStatusHistory.Select(ToResponseHistory).ToList();
    }
}
