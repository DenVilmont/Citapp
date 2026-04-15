using Citapp.BotApi.Infrastructure.Repositories;
using Citapp.Shared.DTOs;

namespace Citapp.BotApi.Services;

public class BookingTransactionService
{
    private readonly BookingRepository _bookings;

    public BookingTransactionService(BookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<BookingDto> CreateAsync(Guid tenantId, CreateBookingDto dto, Guid? createdByUserId)
    {
        return await _bookings.CreateAsync(tenantId, dto, createdByUserId);
    }
}
