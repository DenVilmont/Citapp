using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;

namespace Citapp.Admin.Application;

public sealed class SchedulePageService
{
    private readonly IScheduleRepository _scheduleRepository;

    public SchedulePageService(IScheduleRepository scheduleRepository)
    {
        _scheduleRepository = scheduleRepository;
    }

    public async Task<SchedulePageData> LoadAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        var weekly = await _scheduleRepository.GetWeeklyAsync(tenantId);
        var breaks = await _scheduleRepository.GetBreaksAsync(tenantId);
        var blocked = await _scheduleRepository.GetBlockedAsync(tenantId, from, to);
        return new SchedulePageData(weekly, breaks, blocked);
    }

    public async Task<string?> SaveDayAsync(Guid tenantId, WorkingDayInput day)
    {
        if (day.DayOfWeek is < 0 or > 6) return "Day is invalid.";
        if (!TimeOnly.TryParse(day.StartText, out var startTime) || !TimeOnly.TryParse(day.EndText, out var endTime)) return "Start and end times are required.";
        if (day.IsWorkingDay && startTime >= endTime) return "Start time must be before end time.";

        await _scheduleRepository.UpsertWorkingDayAsync(tenantId, day.DayOfWeek, startTime, endTime, day.IsWorkingDay);
        return null;
    }

    public async Task<string?> AddBreakAsync(Guid tenantId, BreakInput input, bool isWorkingDay)
    {
        if (input.DayOfWeek is < 0 or > 6) return "Day is invalid.";
        if (!isWorkingDay) return "Cannot add a break to a non-working day.";
        if (!TimeOnly.TryParse(input.StartText, out var start) || !TimeOnly.TryParse(input.EndText, out var end)) return "Break start and end are required.";
        if (start >= end) return "Break start must be before end.";

        await _scheduleRepository.AddBreakAsync(tenantId, input.DayOfWeek, start, end, input.Label);
        return null;
    }

    public async Task DeleteBreakAsync(Guid tenantId, Guid breakId)
        => await _scheduleRepository.DeleteBreakAsync(tenantId, breakId);

    public async Task<string?> AddBlockedDateAsync(Guid tenantId, BlockedDateInput input)
    {
        if (!DateOnly.TryParse(input.DateText, out var date)) return "Date is required.";

        var hasStart = !string.IsNullOrWhiteSpace(input.StartText);
        var hasEnd = !string.IsNullOrWhiteSpace(input.EndText);
        if (hasStart != hasEnd) return "Use both start and end, or leave both empty for full day.";

        TimeOnly? start = null;
        TimeOnly? end = null;
        if (hasStart)
        {
            if (!TimeOnly.TryParse(input.StartText, out var parsedStart) || !TimeOnly.TryParse(input.EndText, out var parsedEnd)) return "Blocked interval time is invalid.";
            if (parsedStart >= parsedEnd) return "Blocked start must be before end.";
            start = parsedStart;
            end = parsedEnd;
        }

        await _scheduleRepository.AddBlockedDateAsync(tenantId, date, start, end, input.Reason);
        return null;
    }

    public async Task DeleteBlockedDateAsync(Guid tenantId, Guid blockedDateId)
        => await _scheduleRepository.DeleteBlockedDateAsync(tenantId, blockedDateId);
}

public sealed record SchedulePageData(
    List<WorkingHoursDto> Weekly,
    List<FixedBreakDto> Breaks,
    List<BlockedDateDto> BlockedDates);

public sealed record WorkingDayInput(int DayOfWeek, bool IsWorkingDay, string StartText, string EndText);
public sealed record BreakInput(int DayOfWeek, string StartText, string EndText, string? Label);
public sealed record BlockedDateInput(string DateText, string StartText, string EndText, string? Reason);
