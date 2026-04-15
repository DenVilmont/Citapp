using System.Data;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;
using Npgsql;

namespace Citapp.BotApi.Infrastructure.Repositories;

public class TenantRepository
{
    private readonly string _connectionString;

    public TenantRepository(IConfiguration configuration)
    {
        _connectionString = ResolveConnectionString(configuration);
    }

    public async Task<Guid?> GetByPhoneNumberIdAsync(string phoneNumberId)
    {
        const string sql = """
            select tenant_id
            from whatsapp_connections
            where phone_number_id = @phone_number_id
            order by created_at desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("phone_number_id", phoneNumberId);
        var value = await cmd.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }

    public async Task<Guid?> GetTenantIdByOwnerAsync(Guid ownerUserId)
    {
        const string sql = """
            select id
            from tenants
            where owner_user_id = @owner_user_id
            order by created_at asc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("owner_user_id", ownerUserId);
        var value = await cmd.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }

    public async Task<TenantSettings?> GetSettingsAsync(Guid tenantId)
    {
        const string sql = """
            select id, timezone, slot_step_minutes, default_buffer_minutes
            from tenants
            where id = @tenant_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TenantSettings(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    public async Task<TenantBotSettings?> GetBotSettingsAsync(Guid tenantId)
    {
        const string sql = """
            select id, about_text, greeting_text, booking_enabled, timezone
            from tenants
            where id = @tenant_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TenantBotSettings(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetString(4));
    }

    internal static string ResolveConnectionString(IConfiguration configuration)
    {
        return configuration["SUPABASE_DB_CONNECTION_STRING"]
            ?? configuration.GetConnectionString("Supabase")
            ?? throw new InvalidOperationException("SUPABASE_DB_CONNECTION_STRING is not configured.");
    }
}

public record TenantSettings(Guid TenantId, string Timezone, int SlotStepMinutes, int DefaultBufferMinutes);
public record ServiceSnapshot(Guid ServiceId, bool IsActive, int DurationMinutes, decimal PriceAmount, string Currency);
public record TenantBotSettings(Guid TenantId, string? AboutText, string GreetingText, bool BookingEnabled, string Timezone);
public record ActiveServiceForBot(Guid ServiceId, string Name, int DurationMinutes, decimal PriceAmount, string Currency, bool HasPrimaryImage, string? PrimaryImageUrl);

public class CustomerRepository
{
    private readonly string _connectionString;

    public CustomerRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<CustomerDto?> GetByWaUserIdAsync(Guid tenantId, string waUserId)
    {
        const string sql = """
            select id, tenant_id, wa_user_id, phone, display_name, master_note, created_at, last_seen_at
            from customers
            where tenant_id = @tenant_id and wa_user_id = @wa_user_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("wa_user_id", waUserId);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadCustomer(reader) : null;
    }

    public async Task<CustomerDto> GetOrCreateAsync(Guid tenantId, string waUserId, string displayName)
    {
        var existing = await GetByWaUserIdAsync(tenantId, waUserId);
        if (existing is not null)
        {
            return existing;
        }

        const string insertSql = """
            insert into customers (tenant_id, wa_user_id, display_name)
            values (@tenant_id, @wa_user_id, @display_name)
            returning id, tenant_id, wa_user_id, phone, display_name, master_note, created_at, last_seen_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("wa_user_id", waUserId);
        cmd.Parameters.AddWithValue("display_name", displayName);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Failed to create customer record.");
        }

        return ReadCustomer(reader);
    }

    public async Task UpdateLastSeenAsync(Guid id, Guid tenantId)
    {
        const string sql = """
            update customers
            set last_seen_at = now()
            where id = @id and tenant_id = @tenant_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static CustomerDto ReadCustomer(IDataRecord row)
    {
        return new CustomerDto(
            row.GetGuid(0),
            row.GetGuid(1),
            row.GetString(2),
            row.IsDBNull(3) ? null : row.GetString(3),
            row.GetString(4),
            row.IsDBNull(5) ? null : row.GetString(5),
            row.GetFieldValue<DateTimeOffset>(6),
            row.IsDBNull(7) ? null : row.GetFieldValue<DateTimeOffset>(7));
    }
}

public class BookingRepository
{
    private readonly string _connectionString;

    public BookingRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<BookingDto> CreateAsync(Guid tenantId, CreateBookingDto dto, Guid? createdByUserId)
    {
        const string sql = """
            insert into bookings (
                tenant_id, customer_id, service_id, source, status, date,
                start_at, end_at, duration_snapshot_minutes,
                price_snapshot_amount, currency_snapshot, created_by_user_id
            )
            values (
                @tenant_id, @customer_id, @service_id, @source, @status, @date,
                @start_at, @end_at, @duration_snapshot_minutes,
                @price_snapshot_amount, @currency_snapshot, @created_by_user_id
            )
            returning id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                      duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                      created_by_user_id, cancelled_by, cancelled_at, created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", dto.CustomerId);
        cmd.Parameters.AddWithValue("service_id", dto.ServiceId);
        cmd.Parameters.AddWithValue("source", ToDbText(dto.Source));
        cmd.Parameters.AddWithValue("status", "booked");
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

        return ReadBooking(reader);
    }

    public async Task<List<BookingDto>> GetByDateAsync(Guid tenantId, DateOnly date)
    {
        const string sql = """
            select id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                   duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                   created_by_user_id, cancelled_by, cancelled_at, created_at
            from bookings
            where tenant_id = @tenant_id and date = @date
            order by start_at asc
            """;

        return await QueryBookingsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("tenant_id", tenantId);
            cmd.Parameters.AddWithValue("date", date);
        });
    }

    public async Task<List<BookingDto>> GetFutureByCustomerAsync(Guid tenantId, Guid customerId)
    {
        const string sql = """
            select id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                   duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                   created_by_user_id, cancelled_by, cancelled_at, created_at
            from bookings
            where tenant_id = @tenant_id and customer_id = @customer_id and start_at > now()
            order by start_at asc
            """;

        return await QueryBookingsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("tenant_id", tenantId);
            cmd.Parameters.AddWithValue("customer_id", customerId);
        });
    }

    public async Task<BookingDto?> GetActiveByCustomerAndServiceAsync(Guid tenantId, Guid customerId, Guid serviceId)
    {
        const string sql = """
            select id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                   duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                   created_by_user_id, cancelled_by, cancelled_at, created_at
            from bookings
            where tenant_id = @tenant_id
              and customer_id = @customer_id
              and service_id = @service_id
              and status = 'booked'
              and start_at > now()
            order by start_at asc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", customerId);
        cmd.Parameters.AddWithValue("service_id", serviceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBooking(reader) : null;
    }

    public async Task CancelAsync(Guid id, Guid tenantId, string cancelledBy)
    {
        const string sql = """
            update bookings
            set status = 'cancelled', cancelled_by = @cancelled_by, cancelled_at = now()
            where id = @id and tenant_id = @tenant_id and status = 'booked'
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("cancelled_by", cancelledBy.ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<BookingDto?> GetFutureBookedByIdAndCustomerAsync(Guid tenantId, Guid customerId, Guid bookingId)
    {
        const string sql = """
            select id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                   duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                   created_by_user_id, cancelled_by, cancelled_at, created_at
            from bookings
            where tenant_id = @tenant_id
              and customer_id = @customer_id
              and id = @id
              and status = 'booked'
              and start_at > now()
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", customerId);
        cmd.Parameters.AddWithValue("id", bookingId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBooking(reader) : null;
    }

    public async Task UpdateStatusAsync(Guid id, Guid tenantId, string status)
    {
        const string sql = """
            update bookings
            set status = @status
            where id = @id and tenant_id = @tenant_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("status", status.ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<BookingDto?> GetByIdAsync(Guid tenantId, Guid id)
    {
        const string sql = """
            select id, tenant_id, customer_id, service_id, source, status, date, start_at, end_at,
                   duration_snapshot_minutes, price_snapshot_amount, currency_snapshot,
                   created_by_user_id, cancelled_by, cancelled_at, created_at
            from bookings
            where tenant_id = @tenant_id and id = @id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadBooking(reader) : null;
    }

    private async Task<List<BookingDto>> QueryBookingsAsync(string sql, Action<NpgsqlCommand> configure)
    {
        var items = new List<BookingDto>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        configure(cmd);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(ReadBooking(reader));
        }

        return items;
    }

    private static BookingDto ReadBooking(IDataRecord row)
    {
        return new BookingDto(
            row.GetGuid(0),
            row.GetGuid(1),
            row.GetGuid(2),
            row.GetGuid(3),
            ParseEnum<BookingSource>(row.GetString(4)),
            ParseEnum<BookingStatus>(row.GetString(5)),
            row.GetFieldValue<DateOnly>(6),
            row.GetFieldValue<DateTimeOffset>(7),
            row.GetFieldValue<DateTimeOffset>(8),
            row.GetInt32(9),
            row.GetDecimal(10),
            row.GetString(11),
            row.IsDBNull(12) ? null : row.GetGuid(12),
            row.IsDBNull(13) ? null : ParseEnum<CancelledBy>(row.GetString(13)),
            row.IsDBNull(14) ? null : row.GetFieldValue<DateTimeOffset>(14),
            row.GetFieldValue<DateTimeOffset>(15));
    }

    private static string ToDbText<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum
    {
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.Parse<TEnum>(normalized, ignoreCase: true);
    }
}

public class ServiceRepository
{
    private readonly string _connectionString;

    public ServiceRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<ServiceSnapshot?> GetSnapshotAsync(Guid tenantId, Guid serviceId)
    {
        const string sql = """
            select id, is_active, duration_minutes, price_amount, currency
            from services
            where tenant_id = @tenant_id and id = @service_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("service_id", serviceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ServiceSnapshot(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetInt32(2),
            reader.GetDecimal(3),
            reader.GetString(4));
    }

    public async Task<List<ActiveServiceForBot>> GetActiveForBotAsync(Guid tenantId)
    {
        const string sql = """
            select
                s.id,
                s.name,
                s.duration_minutes,
                s.price_amount,
                s.currency,
                exists(
                    select 1
                    from service_media smx
                    where smx.service_id = s.id and smx.is_primary = true and smx.public_url is not null
                ) as has_primary_image,
                (
                    select sm.public_url
                    from service_media sm
                    where sm.service_id = s.id and sm.is_primary = true and sm.public_url is not null
                    order by sm.sort_order asc
                    limit 1
                ) as primary_image_url
            from services s
            where s.tenant_id = @tenant_id and s.is_active = true
            order by s.sort_order asc, s.created_at asc
            """;

        var items = new List<ActiveServiceForBot>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new ActiveServiceForBot(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return items;
    }

    public async Task<Dictionary<Guid, string>> GetNamesByIdsAsync(Guid tenantId, IEnumerable<Guid> serviceIds)
    {
        var ids = serviceIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        const string sql = """
            select id, name
            from services
            where tenant_id = @tenant_id and id = any(@service_ids)
            """;

        var result = new Dictionary<Guid, string>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("service_ids", ids);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.GetString(1);
        }

        return result;
    }
}

public class ScheduleRepository
{
    private readonly string _connectionString;

    public ScheduleRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId)
    {
        const string sql = """
            select id, tenant_id, day_of_week, start_time, end_time, is_working_day
            from weekly_working_hours
            where tenant_id = @tenant_id
            order by day_of_week asc
            """;

        var items = new List<WorkingHoursDto>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new WorkingHoursDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetFieldValue<TimeOnly>(4),
                reader.GetBoolean(5)));
        }

        return items;
    }

    public async Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId)
    {
        const string sql = """
            select id, tenant_id, day_of_week, start_time, end_time, label
            from fixed_breaks
            where tenant_id = @tenant_id
            """;

        var items = new List<FixedBreakDto>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new FixedBreakDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetFieldValue<TimeOnly>(3),
                reader.GetFieldValue<TimeOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return items;
    }

    public async Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        const string sql = """
            select id, tenant_id, date, start_time, end_time, reason
            from blocked_dates
            where tenant_id = @tenant_id and date >= @from_date and date <= @to_date
            """;

        var items = new List<BlockedDateDto>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("from_date", from);
        cmd.Parameters.AddWithValue("to_date", to);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new BlockedDateDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<TimeOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<TimeOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return items;
    }
}

public class ConversationStateRepository
{
    private readonly string _connectionString;

    public ConversationStateRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<ConversationStateDto?> GetAsync(Guid tenantId, Guid customerId)
    {
        const string sql = """
            select id, tenant_id, customer_id, state, payload_json::text, updated_at, expires_at
            from conversation_states
            where tenant_id = @tenant_id and customer_id = @customer_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", customerId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ConversationStateDto(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            Enum.Parse<BotState>(reader.GetString(3), ignoreCase: true),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    public async Task UpsertAsync(ConversationStateDto state)
    {
        const string sql = """
            insert into conversation_states (id, tenant_id, customer_id, state, payload_json, updated_at, expires_at)
            values (@id, @tenant_id, @customer_id, @state, cast(@payload_json as jsonb), @updated_at, @expires_at)
            on conflict (tenant_id, customer_id)
            do update set
                state = excluded.state,
                payload_json = excluded.payload_json,
                updated_at = excluded.updated_at,
                expires_at = excluded.expires_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", state.Id);
        cmd.Parameters.AddWithValue("tenant_id", state.TenantId);
        cmd.Parameters.AddWithValue("customer_id", state.CustomerId);
        cmd.Parameters.AddWithValue("state", state.State.ToString());
        cmd.Parameters.AddWithValue("payload_json", (object?)state.PayloadJson ?? "{}");
        cmd.Parameters.AddWithValue("updated_at", state.UpdatedAt);
        cmd.Parameters.AddWithValue("expires_at", state.ExpiresAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(Guid tenantId, Guid customerId)
    {
        const string sql = """
            delete from conversation_states
            where tenant_id = @tenant_id and customer_id = @customer_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        cmd.Parameters.AddWithValue("customer_id", customerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc)
    {
        const string sql = """
            delete from conversation_states
            where expires_at <= @now_utc
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("now_utc", nowUtc);
        return await cmd.ExecuteNonQueryAsync();
    }
}

public class WebhookEventRepository
{
    private readonly string _connectionString;

    public WebhookEventRepository(IConfiguration configuration)
    {
        _connectionString = TenantRepository.ResolveConnectionString(configuration);
    }

    public async Task<bool> ExistsAsync(string externalEventId)
    {
        const string sql = """
            select 1
            from inbound_webhook_events
            where external_event_id = @external_event_id
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("external_event_id", externalEventId);
        var value = await cmd.ExecuteScalarAsync();
        return value is not null;
    }

    public async Task SaveAsync(string externalEventId, Guid? tenantId, string payloadJson)
    {
        const string sql = """
            insert into inbound_webhook_events (external_event_id, tenant_id, direction, payload_json)
            values (@external_event_id, @tenant_id, 'inbound', cast(@payload_json as jsonb))
            on conflict (external_event_id)
            do nothing
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("external_event_id", externalEventId);
        cmd.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload_json", payloadJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> TrySaveAsync(string externalEventId, Guid? tenantId, string payloadJson)
    {
        const string sql = """
            insert into inbound_webhook_events (external_event_id, tenant_id, direction, payload_json)
            values (@external_event_id, @tenant_id, 'inbound', cast(@payload_json as jsonb))
            on conflict (external_event_id)
            do nothing
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("external_event_id", externalEventId);
        cmd.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payload_json", payloadJson);
        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }
}
