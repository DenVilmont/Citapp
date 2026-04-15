using Citapp.BotApi.Infrastructure.Repositories;

namespace Citapp.BotApi.Services;

public class SlotCalculationService
{
    private readonly ScheduleRepository _schedule;
    private readonly BookingRepository _bookings;
    public SlotCalculationService(ScheduleRepository schedule, BookingRepository bookings) { _schedule = schedule; _bookings = bookings; }

    public async Task<List<TimeSlot>> GetAvailableSlotsAsync(Guid tenantId, Guid serviceId, DateOnly date)
    {
        var weekly = await _schedule.GetWeeklyAsync(tenantId);
        var day = weekly.FirstOrDefault(x => x.DayOfWeek == (int)date.DayOfWeek);
        if (day is null || !day.IsWorkingDay) return new();
        return new List<TimeSlot>();
    }

    public async Task<List<DateOnly>> GetAvailableDatesAsync(Guid tenantId, Guid serviceId, int horizonDays = 30)
    {
        var output = new List<DateOnly>();
        for (var i = 0; i < horizonDays; i++)
        {
            var d = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(i));
            if ((await GetAvailableSlotsAsync(tenantId, serviceId, d)).Count > 0) output.Add(d);
        }
        return output;
    }
}
