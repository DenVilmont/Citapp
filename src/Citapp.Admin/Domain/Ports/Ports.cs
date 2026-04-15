using Citapp.Shared.DTOs;

namespace Citapp.Admin.Domain.Ports;

public interface IServiceRepository
{
    Task<List<ServiceDto>> GetActiveAsync(Guid tenantId);
    Task<ServiceDto?> GetByIdAsync(Guid id, Guid tenantId);
    Task<ServiceDto> CreateAsync(object dto, Guid tenantId);
    Task<ServiceDto> UpdateAsync(Guid id, object dto, Guid tenantId);
}

public interface IBookingRepository
{
    Task<List<BookingDto>> GetByDateAsync(Guid tenantId, DateOnly date);
    Task<List<BookingDto>> GetFutureByCustomerAsync(Guid tenantId, Guid customerId);
    Task<BookingDto?> GetActiveByCustomerAndServiceAsync(Guid tenantId, Guid customerId, Guid serviceId);
    Task<BookingDto> CreateAsync(CreateBookingDto dto);
    Task CancelAsync(Guid id, Guid tenantId, string cancelledBy);
    Task UpdateStatusAsync(Guid id, Guid tenantId, string status);
}

public interface ICustomerRepository
{
    Task<CustomerDto?> GetByWaUserIdAsync(Guid tenantId, string waUserId);
    Task<CustomerDto> GetOrCreateAsync(Guid tenantId, string waUserId, string displayName);
    Task<List<CustomerDto>> SearchAsync(Guid tenantId, string query);
    Task UpdateNoteAsync(Guid id, Guid tenantId, string note);
    Task UpdateLastSeenAsync(Guid id, Guid tenantId);
}

public interface IScheduleRepository
{
    Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId);
    Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId);
    Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to);
}

public interface IConversationStateRepository
{
    Task<ConversationStateDto?> GetAsync(Guid tenantId, Guid customerId);
    Task UpsertAsync(ConversationStateDto state);
    Task DeleteAsync(Guid tenantId, Guid customerId);
}

public interface ITenantRepository
{
    Task<TenantDto?> GetAsync(Guid tenantId);
    Task<TenantDto?> GetByOwnerAsync(Guid ownerUserId);
    Task<TenantDto> UpdateAsync(Guid tenantId, TenantDto dto);
}
