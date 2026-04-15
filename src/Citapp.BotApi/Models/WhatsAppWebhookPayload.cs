using System.Text.Json.Serialization;

namespace Citapp.BotApi.Models;

public record WhatsAppWebhookPayload([property: JsonPropertyName("object")] string Object, [property: JsonPropertyName("entry")] List<Entry> Entry);
public record Entry([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("changes")] List<Change> Changes);
public record Change([property: JsonPropertyName("field")] string Field, [property: JsonPropertyName("value")] ChangeValue Value);
public record ChangeValue([property: JsonPropertyName("messaging_product")] string MessagingProduct, [property: JsonPropertyName("metadata")] Metadata Metadata,
    [property: JsonPropertyName("contacts")] List<Contact>? Contacts, [property: JsonPropertyName("messages")] List<Message>? Messages,
    [property: JsonPropertyName("statuses")] List<MessageStatus>? Statuses);
public record Metadata([property: JsonPropertyName("display_phone_number")] string DisplayPhoneNumber, [property: JsonPropertyName("phone_number_id")] string PhoneNumberId);
public record Contact([property: JsonPropertyName("profile")] Profile Profile, [property: JsonPropertyName("wa_id")] string WaId);
public record Profile([property: JsonPropertyName("name")] string Name);
public record Message([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("from")] string From, [property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("text")] TextBody? Text, [property: JsonPropertyName("interactive")] InteractiveBody? Interactive);
public record MessageStatus([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("recipient_id")] string? RecipientId);
public record TextBody([property: JsonPropertyName("body")] string Body);
public record InteractiveBody([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("button_reply")] ButtonReply? ButtonReply, [property: JsonPropertyName("list_reply")] ListReply? ListReply);
public record ButtonReply([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("title")] string Title);
public record ListReply([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("title")] string Title);
