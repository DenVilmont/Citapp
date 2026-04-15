using Citapp.Shared.Enums;
using System.Text.Json.Serialization;

namespace Citapp.Shared.DTOs;

public record ProfileDto(Guid UserId, string Email, string? DisplayName, DateTimeOffset CreatedAt);
public record TenantDto(Guid Id, Guid OwnerUserId, string BusinessName, string? AboutText, string GreetingText, string Timezone, string Currency, int SlotStepMinutes, int DefaultBufferMinutes, bool BookingEnabled, DateTimeOffset CreatedAt);
public record TenantMemberDto(Guid TenantId, Guid UserId, string Role);
public record WhatsAppConnectionDto(Guid Id, Guid TenantId, string? WabaId, string PhoneNumberId, string PhoneNumber, string? DisplayName, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ConnectedAt);
public record ServiceDto(Guid Id, Guid TenantId, string Name, string? Description, int DurationMinutes, decimal PriceAmount, string Currency, bool IsActive, int SortOrder, DateTimeOffset CreatedAt);
public record ServiceMediaDto(Guid Id, Guid ServiceId, string StoragePath, string? PublicUrl, bool IsPrimary, int SortOrder);
public record WorkingHoursDto(Guid Id, Guid TenantId, int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, bool IsWorkingDay);
public record FixedBreakDto(Guid Id, Guid TenantId, int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, string? Label);
public record BlockedDateDto(Guid Id, Guid TenantId, DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, string? Reason);
public record CustomerDto(Guid Id, Guid TenantId, string WaUserId, string? Phone, string DisplayName, string? MasterNote, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, int BookingCount);
public record BookingDto(Guid Id, Guid TenantId, Guid CustomerId, Guid ServiceId, BookingSource Source, BookingStatus Status, DateOnly Date, DateTimeOffset StartAt, DateTimeOffset EndAt, int DurationSnapshotMinutes, decimal PriceSnapshotAmount, string CurrencySnapshot, Guid? CreatedByUserId, CancelledBy? CancelledBy, DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt);
public record ConversationStateDto(Guid Id, Guid TenantId, Guid CustomerId, BotState State, string? PayloadJson, DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt);
public record InboundWebhookEventDto(Guid Id, string Provider, string ExternalEventId, Guid? TenantId, string Direction, string PayloadJson, bool Processed, DateTimeOffset CreatedAt);

public record CreateBookingDto(
    Guid CustomerId,
    Guid ServiceId,
    BookingSource Source,
    DateOnly Date,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int DurationSnapshotMinutes,
    decimal PriceSnapshotAmount,
    string CurrencySnapshot)
{
    [JsonIgnore]
    public Guid TenantId { get; init; }
}
