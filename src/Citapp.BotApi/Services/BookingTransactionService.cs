using Citapp.BotApi.Infrastructure.Repositories;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;
using Npgsql;

namespace Citapp.BotApi.Services;

public class BookingTransactionService
{
    private readonly BookingRepository _bookings;
    private readonly ServiceRepository _services;
    private readonly TenantRepository _tenants;
    private readonly string _connectionString;

    public BookingTransactionService(
        BookingRepository bookings,
        ServiceRepository services,
        TenantRepository tenants,
        IConfiguration configuration)
    {
        _bookings = bookings;
        _services = services;
        _tenants = tenants;
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<BookingDto> CreateAsync(Guid tenantId, CreateBookingDto dto, Guid? createdByUserId)
    {
        var tenant = await _tenants.GetSettingsAsync(tenantId) ?? throw new BookingConflictException("Tenant not found.");
        var service = await _services.GetSnapshotAsync(tenantId, dto.ServiceId);
        if (service is null || !service.IsActive)
        {
            throw new BookingConflictException("Service is not available.");
        }

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
        var localStart = TimeZoneInfo.ConvertTime(dto.StartAt, timezone);
        var localDate = DateOnly.FromDateTime(localStart.Date);
        var computedEndUtc = dto.StartAt.ToUniversalTime().AddMinutes(service.DurationMinutes + tenant.DefaultBufferMinutes);

        if (dto.Date != localDate)
        {
            throw new BookingConflictException("Requested date does not match slot start in tenant timezone.");
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await EnsureSlotIsAvailableInTransaction(tenantId, dto.ServiceId, dto.StartAt.ToUniversalTime(), localDate, conn, tx);
            await EnsureNoDuplicateActiveBookingInTransaction(tenantId, dto.CustomerId, dto.ServiceId, conn, tx);

            var create = dto with
            {
                Date = localDate,
                StartAt = dto.StartAt.ToUniversalTime(),
                EndAt = computedEndUtc,
                DurationSnapshotMinutes = service.DurationMinutes,
                PriceSnapshotAmount = service.PriceAmount,
                CurrencySnapshot = service.Currency
            };

            var booking = await CreateInTransaction(tenantId, create, createdByUserId, conn, tx);
            await tx.CommitAsync();
            return booking;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await tx.RollbackAsync();
            throw new BookingConflictException("Slot was just taken. Please choose another time.");
        }
        catch (BookingConflictException)
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateStatusAsync(Guid tenantId, Guid bookingId, string status)
    {
        var booking = await _bookings.GetByIdAsync(tenantId, bookingId);
        if (booking is null)
        {
            throw new BookingConflictException("Booking not found.");
        }

        var target = status.Trim().ToLowerInvariant();
        if (target is not ("complete" or "completed" or "no_show" or "noshow" or "cancel" or "cancelled"))
        {
            throw new BookingConflictException("Unsupported status transition.");
        }

        if (booking.Status != BookingStatus.Booked)
        {
            throw new BookingConflictException("Only booked bookings can be updated.");
        }

        if (target is "cancel" or "cancelled")
        {
            await _bookings.CancelAsync(bookingId, tenantId, "master");
            return;
        }

        var normalized = target is "complete" ? "completed" : target;
        if (normalized == "noshow")
        {
            normalized = "no_show";
        }

        await _bookings.UpdateStatusAsync(bookingId, tenantId, normalized);
    }

    private static async Task EnsureNoDuplicateActiveBookingInTransaction(
        Guid tenantId,
        Guid customerId,
        Guid serviceId,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
    {
        const string sql = """
            select 1
            from bookings
            where tenant_id = @tenant_id
              and customer_id = @customer_id
              and service_id = @service_id
              and status = 'booked'
              and start_at > now()
            limit 1
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", customerId);
        cmd.Parameters.AddWithValue("service_id", serviceId);
        var value = await cmd.ExecuteScalarAsync();
        if (value is not null)
        {
            throw new BookingConflictException("Customer already has an active future booking for this service.");
        }
    }

    private async Task EnsureSlotIsAvailableInTransaction(
        Guid tenantId,
        Guid serviceId,
        DateTimeOffset startUtc,
        DateOnly date,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
    {
        var slots = await GetAvailableSlotsInTransaction(tenantId, serviceId, date, conn, tx);
        if (!slots.Any(x => x.StartAt.ToUniversalTime() == startUtc))
        {
            throw new BookingConflictException("Requested slot is not available.");
        }
    }

    private async Task<List<TimeSlot>> GetAvailableSlotsInTransaction(
        Guid tenantId,
        Guid serviceId,
        DateOnly date,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
    {
        var tenant = await _tenants.GetSettingsAsync(tenantId) ?? throw new BookingConflictException("Tenant not found.");
        var service = await _services.GetSnapshotAsync(tenantId, serviceId);
        if (service is null || !service.IsActive)
        {
            return new List<TimeSlot>();
        }

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
        var weekly = await QueryWorkingDay(tenantId, date, conn, tx);
        if (weekly is null)
        {
            return new List<TimeSlot>();
        }

        var blocked = await QueryBlockedForDate(tenantId, date, conn, tx);
        if (blocked.Any(x => x.StartTime is null && x.EndTime is null))
        {
            return new List<TimeSlot>();
        }

        var breaks = await QueryBreaksForWeekday(tenantId, (int)date.DayOfWeek, conn, tx);
        var existingBookings = await QueryBookedIntervals(tenantId, date, conn, tx);
        var occupiedMinutes = service.DurationMinutes + tenant.DefaultBufferMinutes;

        var workStartUtc = ToUtc(date, weekly.StartTime, timezone);
        var workEndUtc = ToUtc(date, weekly.EndTime, timezone);
        var intervals = new List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>();
        intervals.AddRange(breaks.Select(x => (ToUtc(date, x.StartTime, timezone), ToUtc(date, x.EndTime, timezone))));
        intervals.AddRange(blocked.Where(x => x.StartTime is not null && x.EndTime is not null)
            .Select(x => (ToUtc(date, x.StartTime!.Value, timezone), ToUtc(date, x.EndTime!.Value, timezone))));
        intervals.AddRange(existingBookings);

        var slots = new List<TimeSlot>();
        var step = TimeSpan.FromMinutes(tenant.SlotStepMinutes);
        var occupied = TimeSpan.FromMinutes(occupiedMinutes);
        for (var cursor = workStartUtc; cursor + occupied <= workEndUtc; cursor = cursor.Add(step))
        {
            var candidateEnd = cursor.Add(occupied);
            if (intervals.Any(x => cursor < x.EndUtc && x.StartUtc < candidateEnd))
            {
                continue;
            }

            var localStart = TimeZoneInfo.ConvertTime(cursor, timezone);
            var localEnd = TimeZoneInfo.ConvertTime(candidateEnd, timezone);
            slots.Add(new TimeSlot(localStart, localEnd, localStart.ToString("HH:mm")));
        }

        return slots;
    }

    private static async Task<WorkingHoursDto?> QueryWorkingDay(Guid tenantId, DateOnly date, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        const string sql = """
            select id, tenant_id, day_of_week, start_time, end_time, is_working_day
            from weekly_working_hours
            where tenant_id = @tenant_id and day_of_week = @day_of_week and is_working_day = true
            limit 1
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("day_of_week", (int)date.DayOfWeek);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var result = new WorkingHoursDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetFieldValue<TimeOnly>(3),
            reader.GetFieldValue<TimeOnly>(4),
            reader.GetBoolean(5));
        return result;
    }

    private static async Task<List<FixedBreakDto>> QueryBreaksForWeekday(Guid tenantId, int weekday, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        const string sql = """
            select id, tenant_id, day_of_week, start_time, end_time, label
            from fixed_breaks
            where tenant_id = @tenant_id and day_of_week = @day_of_week
            """;

        var output = new List<FixedBreakDto>();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("day_of_week", weekday);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            output.Add(new FixedBreakDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetFieldValue<TimeOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return output;
    }

    private static async Task<List<BlockedDateDto>> QueryBlockedForDate(Guid tenantId, DateOnly date, NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        const string sql = """
            select id, tenant_id, date, start_time, end_time, reason
            from blocked_dates
            where tenant_id = @tenant_id and date = @date
            """;

        var output = new List<BlockedDateDto>();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("date", date);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            output.Add(new BlockedDateDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<TimeOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<TimeOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return output;
    }

    private static async Task<List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>> QueryBookedIntervals(
        Guid tenantId,
        DateOnly date,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
    {
        const string sql = """
            select start_at, end_at
            from bookings
            where tenant_id = @tenant_id and date = @date and status <> 'cancelled'
            for update
            """;

        var output = new List<(DateTimeOffset StartUtc, DateTimeOffset EndUtc)>();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("date", date);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            output.Add((reader.GetFieldValue<DateTimeOffset>(0).ToUniversalTime(), reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime()));
        }

        return output;
    }

    private static async Task<BookingDto> CreateInTransaction(
        Guid tenantId,
        CreateBookingDto dto,
        Guid? createdByUserId,
        NpgsqlConnection conn,
        NpgsqlTransaction tx)
    {
        const string sql = """
            insert into bookings (
                tenant_id, customer_id, service_id, source, status, date,
                start_at, end_at, duration_snapshot_minutes,
                price_snapshot_amount, currency_snapshot, created_by_user_id
            )
            values (
                @tenant_id, @customer_id, @service_id, @source, 'booked', @date,
                @start_at, @end_at, @duration_snapshot_minutes,
                @price_snapshot_amount, @currency_snapshot, @created_by_user_id
            )
            returning id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                      duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                      created_by_user_id, cancelled_by, cancelled_at, created_at
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", dto.CustomerId);
        cmd.Parameters.AddWithValue("service_id", dto.ServiceId);
        cmd.Parameters.AddWithValue("source", dto.Source.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("date", dto.Date);
        cmd.Parameters.AddWithValue("start_at", dto.StartAt);
        cmd.Parameters.AddWithValue("end_at", dto.EndAt);
        cmd.Parameters.AddWithValue("duration_snapshot_minutes", dto.DurationSnapshotMinutes);
        cmd.Parameters.AddWithValue("price_snapshot_amount", dto.PriceSnapshotAmount);
        cmd.Parameters.AddWithValue("currency_snapshot", dto.CurrencySnapshot);
        cmd.Parameters.AddWithValue("created_by_user_id", (object?)createdByUserId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Failed to create booking record.");
        }

        return new BookingDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            Enum.Parse<BookingSource>(reader.GetString(4), true),
            Enum.Parse<BookingStatus>(reader.GetString(5).Replace("_", string.Empty), true),
            reader.GetFieldValue<DateOnly>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetInt32(9),
            reader.GetDecimal(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : Enum.Parse<CancelledBy>(reader.GetString(13), true),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.GetFieldValue<DateTimeOffset>(15));
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timezone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
