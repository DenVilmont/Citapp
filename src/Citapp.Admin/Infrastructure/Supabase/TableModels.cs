using Postgrest.Attributes;
using Postgrest.Models;

namespace Citapp.Admin.Infrastructure.Supabase;

[Table("profiles")]
public class ProfileRow : BaseModel
{
    [PrimaryKey("user_id", false)] public Guid UserId { get; set; }
    [Column("email")] public string? Email { get; set; }
    [Column("display_name")] public string? DisplayName { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

[Table("tenants")]
public class TenantRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("owner_user_id")] public Guid OwnerUserId { get; set; }
    [Column("business_name")] public string BusinessName { get; set; } = string.Empty;
    [Column("about_text")] public string? AboutText { get; set; }
    [Column("greeting_text")] public string GreetingText { get; set; } = "";
    [Column("timezone")] public string Timezone { get; set; } = "Europe/Madrid";
    [Column("currency")] public string Currency { get; set; } = "EUR";
    [Column("slot_step_minutes")] public int SlotStepMinutes { get; set; }
    [Column("default_buffer_minutes")] public int DefaultBufferMinutes { get; set; }
    [Column("booking_enabled")] public bool BookingEnabled { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

[Table("whatsapp_connections")]
public class WhatsAppConnectionRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("waba_id")] public string? WabaId { get; set; }
    [Column("phone_number_id")] public string PhoneNumberId { get; set; } = string.Empty;
    [Column("phone_number")] public string PhoneNumber { get; set; } = string.Empty;
    [Column("display_name")] public string? DisplayName { get; set; }
    [Column("status")] public string Status { get; set; } = "unknown";
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("connected_at")] public DateTimeOffset? ConnectedAt { get; set; }
}

[Table("services")]
public class ServiceRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("duration_minutes")] public int DurationMinutes { get; set; }
    [Column("price_amount")] public decimal PriceAmount { get; set; }
    [Column("currency")] public string Currency { get; set; } = "EUR";
    [Column("is_active")] public bool IsActive { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

[Table("weekly_working_hours")]
public class WorkingHoursRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("day_of_week")] public int DayOfWeek { get; set; }
    [Column("start_time")] public TimeOnly StartTime { get; set; }
    [Column("end_time")] public TimeOnly EndTime { get; set; }
    [Column("is_working_day")] public bool IsWorkingDay { get; set; }
}

[Table("fixed_breaks")]
public class FixedBreakRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("day_of_week")] public int DayOfWeek { get; set; }
    [Column("start_time")] public TimeOnly StartTime { get; set; }
    [Column("end_time")] public TimeOnly EndTime { get; set; }
    [Column("label")] public string? Label { get; set; }
}

[Table("blocked_dates")]
public class BlockedDateRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("date")] public DateOnly Date { get; set; }
    [Column("start_time")] public TimeOnly? StartTime { get; set; }
    [Column("end_time")] public TimeOnly? EndTime { get; set; }
    [Column("reason")] public string? Reason { get; set; }
}

[Table("customers")]
public class CustomerRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("wa_user_id")] public string WaUserId { get; set; } = string.Empty;
    [Column("phone")] public string? Phone { get; set; }
    [Column("display_name")] public string DisplayName { get; set; } = string.Empty;
    [Column("master_note")] public string? MasterNote { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
}

[Table("bookings")]
public class BookingRow : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("tenant_id")] public Guid TenantId { get; set; }
    [Column("customer_id")] public Guid CustomerId { get; set; }
    [Column("service_id")] public Guid ServiceId { get; set; }
    [Column("source")] public string Source { get; set; } = "admin";
    [Column("status")] public string Status { get; set; } = "booked";
    [Column("date")] public DateOnly Date { get; set; }
    [Column("start_at")] public DateTimeOffset StartAt { get; set; }
    [Column("end_at")] public DateTimeOffset EndAt { get; set; }
    [Column("duration_snapshot_minutes")] public int DurationSnapshotMinutes { get; set; }
    [Column("price_snapshot_amount")] public decimal PriceSnapshotAmount { get; set; }
    [Column("currency_snapshot")] public string CurrencySnapshot { get; set; } = "EUR";
    [Column("created_by_user_id")] public Guid? CreatedByUserId { get; set; }
    [Column("cancelled_by")] public string? CancelledBy { get; set; }
    [Column("cancelled_at")] public DateTimeOffset? CancelledAt { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
}
