using Citapp.Shared.DTOs;

namespace Citapp.BotApi.Services;

public class BookingTransactionService
{
    public Task<BookingDto> CreateAsync(CreateBookingDto dto)
    {
        var booking = new BookingDto(Guid.NewGuid(), dto.TenantId, dto.CustomerId, dto.ServiceId, dto.Source, Citapp.Shared.Enums.BookingStatus.Booked, dto.Date, dto.StartAt, dto.EndAt, dto.DurationSnapshotMinutes, dto.PriceSnapshotAmount, dto.CurrencySnapshot, null, null, null, DateTimeOffset.UtcNow);
        return Task.FromResult(booking);
    }
}
