using Citapp.BotApi.Infrastructure.Repositories;

namespace Citapp.BotApi.Services;

public class SlotCalculationService
{
    private readonly ScheduleRepository _schedule;
    private readonly BookingRepository _bookings;
    private readonly ServiceRepository _services;
    private readonly TenantRepository _tenants;

    public SlotCalculationService(
        ScheduleRepository schedule,
        BookingRepository bookings,
        ServiceRepository services,
        TenantRepository tenants)
    {
        _schedule = schedule;
        _bookings = bookings;
        _services = services;
        _tenants = tenants;
    }

    public async Task<List<TimeSlot>> GetAvailableSlotsAsync(Guid tenantId, Guid serviceId, DateOnly date)
    {
        var tenant = await _tenants.GetSettingsAsync(tenantId) ?? throw new InvalidOperationException("Tenant not found.");
        var service = await _services.GetSnapshotAsync(tenantId, serviceId);
        if (service is null || !service.IsActive)
        {
            return new List<TimeSlot>();
        }

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
        var nowUtc = DateTimeOffset.UtcNow;
        var weekly = await _schedule.GetWeeklyAsync(tenantId);
        var day = weekly.FirstOrDefault(x => x.DayOfWeek == (int)date.DayOfWeek);
        if (day is null || !day.IsWorkingDay) return new();

        var blocked = await _schedule.GetBlockedAsync(tenantId, date, date);
        var dayBlocked = blocked.Where(x => x.Date == date).ToList();
        if (dayBlocked.Any(x => x.StartTime is null && x.EndTime is null))
        {
            return new List<TimeSlot>();
        }

        var breaks = (await _schedule.GetBreaksAsync(tenantId)).Where(x => x.DayOfWeek == (int)date.DayOfWeek).ToList();
        var bookings = (await _bookings.GetByDateAsync(tenantId, date))
            .Where(x => x.Status != Citapp.Shared.Enums.BookingStatus.Cancelled)
            .ToList();

        var occupancyMinutes = service.DurationMinutes + tenant.DefaultBufferMinutes;
        var workStartUtc = ToUtc(date, day.StartTime, timezone);
        var workEndUtc = ToUtc(date, day.EndTime, timezone);
        var intervals = new List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>();

        intervals.AddRange(breaks.Select(x => (ToUtc(date, x.StartTime, timezone), ToUtc(date, x.EndTime, timezone))));
        intervals.AddRange(dayBlocked
            .Where(x => x.StartTime is not null && x.EndTime is not null)
            .Select(x => (ToUtc(date, x.StartTime!.Value, timezone), ToUtc(date, x.EndTime!.Value, timezone))));
        intervals.AddRange(bookings.Select(x => (x.StartAt.ToUniversalTime(), x.EndAt.ToUniversalTime())));

        var slots = new List<TimeSlot>();
        var step = TimeSpan.FromMinutes(tenant.SlotStepMinutes);
        var occupied = TimeSpan.FromMinutes(occupancyMinutes);

        for (var cursor = workStartUtc; cursor + occupied <= workEndUtc; cursor = cursor.Add(step))
        {
            var candidateStart = cursor;
            var candidateEnd = candidateStart.Add(occupied);
            if (candidateStart <= nowUtc)
            {
                continue;
            }

            if (intervals.Any(x => Intersects(candidateStart, candidateEnd, x.StartUtc, x.EndUtc)))
            {
                continue;
            }

            var localStart = TimeZoneInfo.ConvertTime(candidateStart, timezone);
            var localEnd = TimeZoneInfo.ConvertTime(candidateEnd, timezone);
            slots.Add(new TimeSlot(localStart, localEnd, localStart.ToString("HH:mm")));
        }

        return slots;
    }

    public async Task<List<DateOnly>> GetAvailableDatesAsync(Guid tenantId, Guid serviceId, int horizonDays = 30)
    {
        var tenant = await _tenants.GetSettingsAsync(tenantId) ?? throw new InvalidOperationException("Tenant not found.");
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone).Date);
        var output = new List<DateOnly>();
        for (var i = 0; i < horizonDays; i++)
        {
            var d = todayLocal.AddDays(i);
            if ((await GetAvailableSlotsAsync(tenantId, serviceId, d)).Count > 0) output.Add(d);
        }
        return output;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timezone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static bool Intersects(DateTimeOffset start1, DateTimeOffset end1, DateTimeOffset start2, DateTimeOffset end2)
    {
        return start1 < end2 && start2 < end1;
    }
}
