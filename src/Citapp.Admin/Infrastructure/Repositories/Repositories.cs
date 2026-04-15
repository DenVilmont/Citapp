using Citapp.Admin.Domain.Ports;
using Citapp.Admin.Infrastructure.Supabase;
using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;
using Postgrest.Constants;
using Supabase;

namespace Citapp.Admin.Infrastructure.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly Client _client;
    public ServiceRepository(Client client) => _client = client;

    public async Task<List<ServiceDto>> GetAllAsync(Guid tenantId)
    {
        var response = await _client.From<ServiceRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Order("sort_order", Ordering.Ascending)
            .Get();

        return response.Models.Select(ToDto).ToList();
    }

    public async Task<ServiceDto?> GetByIdAsync(Guid id, Guid tenantId)
    {
        var response = await _client.From<ServiceRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        return response.Models.Select(ToDto).FirstOrDefault();
    }

    public async Task<ServiceDto> CreateAsync(ServiceDto dto, Guid tenantId)
    {
        var row = new ServiceRow
        {
            TenantId = tenantId,
            Name = dto.Name,
            Description = dto.Description,
            DurationMinutes = dto.DurationMinutes,
            PriceAmount = dto.PriceAmount,
            Currency = dto.Currency,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder
        };

        var response = await _client.From<ServiceRow>().Insert(row);
        return ToDto(response.Models.First());
    }

    public async Task<ServiceDto> UpdateAsync(Guid id, ServiceDto dto, Guid tenantId)
    {
        var existing = await _client.From<ServiceRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        var row = existing.Models.First();
        row.Name = dto.Name;
        row.Description = dto.Description;
        row.DurationMinutes = dto.DurationMinutes;
        row.PriceAmount = dto.PriceAmount;
        row.Currency = dto.Currency;
        row.SortOrder = dto.SortOrder;
        row.IsActive = dto.IsActive;

        var response = await row.Update<ServiceRow>();
        return ToDto(response.Models.First());
    }

    public async Task SetActiveAsync(Guid id, Guid tenantId, bool isActive)
    {
        var existing = await _client.From<ServiceRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        var row = existing.Models.First();
        row.IsActive = isActive;
        await row.Update<ServiceRow>();
    }

    private static ServiceDto ToDto(ServiceRow r)
        => new(r.Id, r.TenantId, r.Name, r.Description, r.DurationMinutes, r.PriceAmount, r.Currency, r.IsActive, r.SortOrder, r.CreatedAt);
}

public class BookingRepository : IBookingRepository
{
    private readonly Client _client;
    public BookingRepository(Client client) => _client = client;

    public async Task<List<BookingDto>> GetUpcomingAsync(Guid tenantId, DateTimeOffset fromUtc)
    {
        var response = await _client.From<BookingRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Filter("start_at", Operator.GreaterThanOrEqual, fromUtc.UtcDateTime.ToString("O"))
            .Order("start_at", Ordering.Ascending)
            .Get();

        return response.Models.Select(ToDto).ToList();
    }

    public async Task<List<BookingDto>> GetForRangeAsync(Guid tenantId, DateOnly from, DateOnly to, BookingStatus? status)
    {
        var query = _client.From<BookingRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Filter("date", Operator.GreaterThanOrEqual, from.ToString("yyyy-MM-dd"))
            .Filter("date", Operator.LessThanOrEqual, to.ToString("yyyy-MM-dd"));

        if (status is not null)
        {
            query = query.Filter("status", Operator.Equals, status.Value.ToString().ToLowerInvariant());
        }

        var response = await query.Order("start_at", Ordering.Ascending).Get();
        return response.Models.Select(ToDto).ToList();
    }

    private static BookingDto ToDto(BookingRow r)
    {
        Enum.TryParse<BookingSource>(r.Source, true, out var source);
        Enum.TryParse<BookingStatus>(r.Status, true, out var status);
        CancelledBy? cancelledBy = null;
        if (!string.IsNullOrWhiteSpace(r.CancelledBy) && Enum.TryParse<CancelledBy>(r.CancelledBy, true, out var parsedCancelled))
        {
            cancelledBy = parsedCancelled;
        }

        return new BookingDto(
            r.Id,
            r.TenantId,
            r.CustomerId,
            r.ServiceId,
            source,
            status,
            r.Date,
            r.StartAt,
            r.EndAt,
            r.DurationSnapshotMinutes,
            r.PriceSnapshotAmount,
            r.CurrencySnapshot,
            r.CreatedByUserId,
            cancelledBy,
            r.CancelledAt,
            r.CreatedAt);
    }
}

public class CustomerRepository : ICustomerRepository
{
    private readonly Client _client;
    public CustomerRepository(Client client) => _client = client;

    public async Task<List<CustomerDto>> SearchAsync(Guid tenantId, string query)
    {
        var response = await _client.From<CustomerRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Order("created_at", Ordering.Descending)
            .Get();

        var normalized = query.Trim();
        return response.Models
            .Where(x => string.IsNullOrWhiteSpace(normalized)
                        || x.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || x.WaUserId.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || (x.Phone?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(ToDto)
            .ToList();
    }

    public async Task<CustomerDto?> GetByIdAsync(Guid tenantId, Guid customerId)
    {
        var response = await _client.From<CustomerRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Filter("id", Operator.Equals, customerId.ToString())
            .Limit(1)
            .Get();

        return response.Models.Select(ToDto).FirstOrDefault();
    }

    public async Task UpdateNoteAsync(Guid id, Guid tenantId, string note)
    {
        var response = await _client.From<CustomerRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        var row = response.Models.First();
        row.MasterNote = note;
        await row.Update<CustomerRow>();
    }

    private static CustomerDto ToDto(CustomerRow r)
        => new(r.Id, r.TenantId, r.WaUserId, r.Phone, r.DisplayName, r.MasterNote, r.CreatedAt, r.LastSeenAt);
}

public class ScheduleRepository : IScheduleRepository
{
    private readonly Client _client;
    public ScheduleRepository(Client client) => _client = client;

    public async Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId)
    {
        var response = await _client.From<WorkingHoursRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Order("day_of_week", Ordering.Ascending)
            .Get();

        return response.Models.Select(x => new WorkingHoursDto(x.Id, x.TenantId, x.DayOfWeek, x.StartTime, x.EndTime, x.IsWorkingDay)).ToList();
    }

    public async Task UpsertWorkingDayAsync(Guid tenantId, int dayOfWeek, TimeOnly startTime, TimeOnly endTime, bool isWorkingDay)
    {
        var existing = await _client.From<WorkingHoursRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Filter("day_of_week", Operator.Equals, dayOfWeek.ToString())
            .Limit(1)
            .Get();

        if (existing.Models.Count == 0)
        {
            await _client.From<WorkingHoursRow>().Insert(new WorkingHoursRow
            {
                TenantId = tenantId,
                DayOfWeek = dayOfWeek,
                StartTime = startTime,
                EndTime = endTime,
                IsWorkingDay = isWorkingDay
            });
            return;
        }

        var row = existing.Models.First();
        row.StartTime = startTime;
        row.EndTime = endTime;
        row.IsWorkingDay = isWorkingDay;
        await row.Update<WorkingHoursRow>();
    }

    public async Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId)
    {
        var response = await _client.From<FixedBreakRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Order("day_of_week", Ordering.Ascending)
            .Get();

        return response.Models.Select(x => new FixedBreakDto(x.Id, x.TenantId, x.DayOfWeek, x.StartTime, x.EndTime, x.Label)).ToList();
    }

    public async Task<FixedBreakDto> AddBreakAsync(Guid tenantId, int dayOfWeek, TimeOnly startTime, TimeOnly endTime, string? label)
    {
        var response = await _client.From<FixedBreakRow>().Insert(new FixedBreakRow
        {
            TenantId = tenantId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            Label = label
        });

        var row = response.Models.First();
        return new FixedBreakDto(row.Id, row.TenantId, row.DayOfWeek, row.StartTime, row.EndTime, row.Label);
    }

    public async Task DeleteBreakAsync(Guid tenantId, Guid breakId)
    {
        await _client.From<FixedBreakRow>()
            .Filter("id", Operator.Equals, breakId.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Delete();
    }

    public async Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        var response = await _client.From<BlockedDateRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Filter("date", Operator.GreaterThanOrEqual, from.ToString("yyyy-MM-dd"))
            .Filter("date", Operator.LessThanOrEqual, to.ToString("yyyy-MM-dd"))
            .Order("date", Ordering.Ascending)
            .Get();

        return response.Models.Select(x => new BlockedDateDto(x.Id, x.TenantId, x.Date, x.StartTime, x.EndTime, x.Reason)).ToList();
    }

    public async Task<BlockedDateDto> AddBlockedDateAsync(Guid tenantId, DateOnly date, TimeOnly? startTime, TimeOnly? endTime, string? reason)
    {
        var response = await _client.From<BlockedDateRow>().Insert(new BlockedDateRow
        {
            TenantId = tenantId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            Reason = reason
        });

        var row = response.Models.First();
        return new BlockedDateDto(row.Id, row.TenantId, row.Date, row.StartTime, row.EndTime, row.Reason);
    }

    public async Task DeleteBlockedDateAsync(Guid tenantId, Guid blockedDateId)
    {
        await _client.From<BlockedDateRow>()
            .Filter("id", Operator.Equals, blockedDateId.ToString())
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Delete();
    }
}

public class TenantRepository : ITenantRepository
{
    private readonly Client _client;
    public TenantRepository(Client client) => _client = client;

    public async Task<TenantDto?> GetByOwnerAsync(Guid ownerUserId)
    {
        var response = await _client.From<TenantRow>()
            .Filter("owner_user_id", Operator.Equals, ownerUserId.ToString())
            .Order("created_at", Ordering.Ascending)
            .Limit(1)
            .Get();

        return response.Models.Select(ToDto).FirstOrDefault();
    }

    public async Task<TenantDto?> GetAsync(Guid tenantId)
    {
        var response = await _client.From<TenantRow>()
            .Filter("id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        return response.Models.Select(ToDto).FirstOrDefault();
    }

    public async Task<TenantDto> UpsertDefaultTenantAsync(Guid ownerUserId, string email)
    {
        var existing = await GetByOwnerAsync(ownerUserId);
        if (existing is not null) return existing;

        var response = await _client.From<TenantRow>().Insert(new TenantRow
        {
            OwnerUserId = ownerUserId,
            BusinessName = string.IsNullOrWhiteSpace(email) ? "My Business" : $"{email.Split('@')[0]} business",
            GreetingText = "Hello! Choose a service and time.",
            Timezone = "Europe/Madrid",
            Currency = "EUR",
            SlotStepMinutes = 15,
            DefaultBufferMinutes = 0,
            BookingEnabled = true
        });

        return ToDto(response.Models.First());
    }

    public async Task<TenantDto> UpdateAsync(Guid tenantId, TenantDto dto)
    {
        var response = await _client.From<TenantRow>()
            .Filter("id", Operator.Equals, tenantId.ToString())
            .Limit(1)
            .Get();

        var row = response.Models.First();
        row.BusinessName = dto.BusinessName;
        row.AboutText = dto.AboutText;
        row.GreetingText = dto.GreetingText;
        row.Timezone = dto.Timezone;
        row.Currency = dto.Currency;
        row.SlotStepMinutes = dto.SlotStepMinutes;
        row.DefaultBufferMinutes = dto.DefaultBufferMinutes;
        row.BookingEnabled = dto.BookingEnabled;

        var updated = await row.Update<TenantRow>();
        return ToDto(updated.Models.First());
    }

    public async Task<WhatsAppConnectionDto?> GetWhatsAppConnectionAsync(Guid tenantId)
    {
        var response = await _client.From<WhatsAppConnectionRow>()
            .Filter("tenant_id", Operator.Equals, tenantId.ToString())
            .Order("created_at", Ordering.Descending)
            .Limit(1)
            .Get();

        var row = response.Models.FirstOrDefault();
        return row is null
            ? null
            : new WhatsAppConnectionDto(row.Id, row.TenantId, row.WabaId, row.PhoneNumberId, row.PhoneNumber, row.DisplayName, row.Status, row.CreatedAt, row.ConnectedAt);
    }

    private static TenantDto ToDto(TenantRow row)
        => new(row.Id, row.OwnerUserId, row.BusinessName, row.AboutText, row.GreetingText, row.Timezone, row.Currency, row.SlotStepMinutes, row.DefaultBufferMinutes, row.BookingEnabled, row.CreatedAt);
}

public class ProfileRepository : IProfileRepository
{
    private readonly Client _client;
    public ProfileRepository(Client client) => _client = client;

    public async Task<ProfileDto?> GetAsync(Guid userId)
    {
        var response = await _client.From<ProfileRow>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Limit(1)
            .Get();

        var row = response.Models.FirstOrDefault();
        return row is null ? null : new ProfileDto(row.UserId, row.Email ?? string.Empty, row.DisplayName, row.CreatedAt);
    }

    public async Task<ProfileDto> UpsertAsync(ProfileDto dto)
    {
        var existing = await GetAsync(dto.UserId);
        if (existing is null)
        {
            var inserted = await _client.From<ProfileRow>().Insert(new ProfileRow
            {
                UserId = dto.UserId,
                Email = dto.Email,
                DisplayName = dto.DisplayName
            });

            var row = inserted.Models.First();
            return new ProfileDto(row.UserId, row.Email ?? dto.Email, row.DisplayName, row.CreatedAt);
        }

        var response = await _client.From<ProfileRow>()
            .Filter("user_id", Operator.Equals, dto.UserId.ToString())
            .Limit(1)
            .Get();

        var existingRow = response.Models.First();
        existingRow.Email = dto.Email;
        existingRow.DisplayName = dto.DisplayName;

        var updated = await existingRow.Update<ProfileRow>();
        var rowUpdated = updated.Models.First();
        return new ProfileDto(rowUpdated.UserId, rowUpdated.Email ?? dto.Email, rowUpdated.DisplayName, rowUpdated.CreatedAt);
    }
}
