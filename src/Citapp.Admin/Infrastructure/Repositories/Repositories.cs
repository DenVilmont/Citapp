using Citapp.Admin.Domain.Ports;
using Citapp.Shared.DTOs;
using Supabase;

namespace Citapp.Admin.Infrastructure.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly Client _client;
    public ServiceRepository(Client client) => _client = client;
    public Task<ServiceDto> CreateAsync(object dto, Guid tenantId) => Task.FromResult(new ServiceDto(Guid.NewGuid(), tenantId, "", null, 0, 0, "EUR", true, 0, DateTimeOffset.UtcNow));
    public Task<List<ServiceDto>> GetActiveAsync(Guid tenantId) => Task.FromResult(new List<ServiceDto>());
    public Task<ServiceDto?> GetByIdAsync(Guid id, Guid tenantId) => Task.FromResult<ServiceDto?>(null);
    public Task<ServiceDto> UpdateAsync(Guid id, object dto, Guid tenantId) => Task.FromResult(new ServiceDto(id, tenantId, "", null, 0, 0, "EUR", true, 0, DateTimeOffset.UtcNow));
}

public class BookingRepository : IBookingRepository
{
    public Task CancelAsync(Guid id, Guid tenantId, string cancelledBy) => Task.CompletedTask;
    public Task<BookingDto> CreateAsync(CreateBookingDto dto) => Task.FromResult(new BookingDto(Guid.NewGuid(), dto.TenantId, dto.CustomerId, dto.ServiceId, dto.Source, Citapp.Shared.Enums.BookingStatus.Booked, dto.Date, dto.StartAt, dto.EndAt, dto.DurationSnapshotMinutes, dto.PriceSnapshotAmount, dto.CurrencySnapshot, null, null, null, DateTimeOffset.UtcNow));
    public Task<BookingDto?> GetActiveByCustomerAndServiceAsync(Guid tenantId, Guid customerId, Guid serviceId) => Task.FromResult<BookingDto?>(null);
    public Task<List<BookingDto>> GetByDateAsync(Guid tenantId, DateOnly date) => Task.FromResult(new List<BookingDto>());
    public Task<List<BookingDto>> GetFutureByCustomerAsync(Guid tenantId, Guid customerId) => Task.FromResult(new List<BookingDto>());
    public Task UpdateStatusAsync(Guid id, Guid tenantId, string status) => Task.CompletedTask;
}

public class CustomerRepository : ICustomerRepository
{
    public Task<CustomerDto?> GetByWaUserIdAsync(Guid tenantId, string waUserId) => Task.FromResult<CustomerDto?>(null);
    public Task<CustomerDto> GetOrCreateAsync(Guid tenantId, string waUserId, string displayName) => Task.FromResult(new CustomerDto(Guid.NewGuid(), tenantId, waUserId, null, displayName, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    public Task<List<CustomerDto>> SearchAsync(Guid tenantId, string query) => Task.FromResult(new List<CustomerDto>());
    public Task UpdateLastSeenAsync(Guid id, Guid tenantId) => Task.CompletedTask;
    public Task UpdateNoteAsync(Guid id, Guid tenantId, string note) => Task.CompletedTask;
}

public class ScheduleRepository : IScheduleRepository
{
    public Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId) => Task.FromResult(new List<FixedBreakDto>());
    public Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to) => Task.FromResult(new List<BlockedDateDto>());
    public Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId) => Task.FromResult(new List<WorkingHoursDto>());
}

public class ConversationStateRepository : IConversationStateRepository
{
    public Task DeleteAsync(Guid tenantId, Guid customerId) => Task.CompletedTask;
    public Task<ConversationStateDto?> GetAsync(Guid tenantId, Guid customerId) => Task.FromResult<ConversationStateDto?>(null);
    public Task UpsertAsync(ConversationStateDto state) => Task.CompletedTask;
}

public class TenantRepository : ITenantRepository
{
    public Task<TenantDto?> GetAsync(Guid tenantId) => Task.FromResult<TenantDto?>(null);
    public Task<TenantDto?> GetByOwnerAsync(Guid ownerUserId) => Task.FromResult<TenantDto?>(null);
    public Task<TenantDto> UpdateAsync(Guid tenantId, TenantDto dto) => Task.FromResult(dto);
}
