using Citapp.Shared.DTOs;
using Citapp.Shared.Enums;

namespace Citapp.BotApi.Infrastructure.Repositories;

public class TenantRepository
{
    public Task<Guid?> GetByPhoneNumberIdAsync(string phoneNumberId) => Task.FromResult<Guid?>(null);
    public Task<Guid?> GetTenantIdByOwnerAsync(Guid ownerUserId) => Task.FromResult<Guid?>(null);
}

public class CustomerRepository
{
    public Task<CustomerDto?> GetByWaUserIdAsync(Guid tenantId, string waUserId) => Task.FromResult<CustomerDto?>(null);
    public Task<CustomerDto> GetOrCreateAsync(Guid tenantId, string waUserId, string displayName) => Task.FromResult(new CustomerDto(Guid.NewGuid(), tenantId, waUserId, null, displayName, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    public Task UpdateLastSeenAsync(Guid id, Guid tenantId) => Task.CompletedTask;
}

public class BookingRepository
{
    public Task<List<BookingDto>> GetByDateAsync(Guid tenantId, DateOnly date) => Task.FromResult(new List<BookingDto>());
    public Task<List<BookingDto>> GetFutureByCustomerAsync(Guid tenantId, Guid customerId) => Task.FromResult(new List<BookingDto>());
    public Task<BookingDto?> GetActiveByCustomerAndServiceAsync(Guid tenantId, Guid customerId, Guid serviceId) => Task.FromResult<BookingDto?>(null);
    public Task CancelAsync(Guid id, Guid tenantId, string cancelledBy) => Task.CompletedTask;
    public Task UpdateStatusAsync(Guid id, Guid tenantId, string status) => Task.CompletedTask;
}

public class ScheduleRepository
{
    public Task<List<WorkingHoursDto>> GetWeeklyAsync(Guid tenantId) => Task.FromResult(new List<WorkingHoursDto>());
    public Task<List<FixedBreakDto>> GetBreaksAsync(Guid tenantId) => Task.FromResult(new List<FixedBreakDto>());
    public Task<List<BlockedDateDto>> GetBlockedAsync(Guid tenantId, DateOnly from, DateOnly to) => Task.FromResult(new List<BlockedDateDto>());
}

public class ConversationStateRepository
{
    public Task<ConversationStateDto?> GetAsync(Guid tenantId, Guid customerId) => Task.FromResult<ConversationStateDto?>(null);
    public Task UpsertAsync(ConversationStateDto state) => Task.CompletedTask;
    public Task DeleteAsync(Guid tenantId, Guid customerId) => Task.CompletedTask;
}

public class WebhookEventRepository
{
    public Task<bool> ExistsAsync(string externalEventId) => Task.FromResult(false);
    public Task SaveAsync(string externalEventId, Guid? tenantId, string payloadJson) => Task.CompletedTask;
}
