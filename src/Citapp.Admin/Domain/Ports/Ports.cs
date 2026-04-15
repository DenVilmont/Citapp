using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;

namespace Citapp.Admin.Domain.Ports;

public interface IServiceRepository
{
    Task<List<ServiceDto>> GetAllAsync(Guid tenantId);
    Task<List<ServiceDto>> GetByIdsAsync(Guid tenantId, IEnumerable<Guid> serviceIds);
    Task<ServiceDto?> GetByIdAsync(Guid id, Guid tenantId);
    Task<ServiceDto> CreateAsync(ServiceDto dto, Guid tenantId);
    Task<ServiceDto> UpdateAsync(Guid id, ServiceDto dto, Guid tenantId);
    Task SetActiveAsync(Guid id, Guid tenantId, bool isActive);
}

public interface IBookingRepository
{
    Task<List<BookingDto>> GetUpcomingAsync(Guid tenantId, DateTimeOffset fromUtc);
    Task<List<BookingDto>> GetForRangeAsync(Guid tenantId, DateOnly from, DateOnly to, BookingStatus? status);
    Task<List<BookingDto>> GetByCustomerAsync(Guid tenantId, Guid customerId);
}

public interface ICustomerRepository
{
    Task<List<CustomerDto>> SearchAsync(Guid tenantId, string query);
    Task<List<CustomerDto>> GetByIdsAsync(Guid tenantId, IEnumerable<Guid> customerIds);
    Task<CustomerDto?> GetByIdAsync(Guid tenantId, Guid customerId);
    Task<CustomerDto> CreateManualAsync(Guid tenantId, string displayName, string? phone);
    Task UpdateNoteAsync(Guid id, Guid tenantId, string note);
}

public interface IScheduleRepository
{
    Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId);
    Task UpsertWorkingDayAsync(Guid tenantId, int dayOfWeek, TimeOnly startTime, TimeOnly endTime, bool isWorkingDay);

    Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId);
    Task<FixedBreakDto> AddBreakAsync(Guid tenantId, int dayOfWeek, TimeOnly startTime, TimeOnly endTime, string? label);
    Task DeleteBreakAsync(Guid tenantId, Guid breakId);

    Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to);
    Task<BlockedDateDto> AddBlockedDateAsync(Guid tenantId, DateOnly date, TimeOnly? startTime, TimeOnly? endTime, string? reason);
    Task DeleteBlockedDateAsync(Guid tenantId, Guid blockedDateId);
}

public interface ITenantRepository
{
    Task<TenantDto?> GetByOwnerAsync(Guid ownerUserId);
    Task<TenantDto?> GetAsync(Guid tenantId);
    Task<TenantDto> UpsertDefaultTenantAsync(Guid ownerUserId, string email);
    Task<TenantDto> UpdateAsync(Guid tenantId, TenantDto dto);
    Task<WhatsAppConnectionDto?> GetWhatsAppConnectionAsync(Guid tenantId);
}

public interface IProfileRepository
{
    Task<ProfileDto?> GetAsync(Guid userId);
    Task<ProfileDto> UpsertAsync(ProfileDto dto);
}
