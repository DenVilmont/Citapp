using System.Net.Http.Json;

namespace Citapp.BotApi.Services;

public class WhatsAppMessageSender
{
    private readonly HttpClient _http;
    public WhatsAppMessageSender(HttpClient http) => _http = http;

    public Task SendTextAsync(string to, string phoneNumberId, string text)
        => SendAsync(phoneNumberId, new WhatsAppTextRequest(to, text));

    public Task SendButtonsAsync(string to, string phoneNumberId, string body, List<(string Id, string Title)> buttons)
    {
        if (buttons.Count > 3) throw new ArgumentException("WhatsApp buttons limit is 3.");
        return SendAsync(phoneNumberId, new WhatsAppButtonsRequest(to, body, buttons));
    }

    public Task SendListAsync(string to, string phoneNumberId, string body, string buttonLabel, List<(string Id, string Title, string Description)> rows)
    {
        if (rows.Count > 10) throw new ArgumentException("WhatsApp list rows limit is 10.");
        return SendAsync(phoneNumberId, new WhatsAppListRequest(to, body, buttonLabel, rows));
    }

    public Task SendImageAsync(string to, string phoneNumberId, string imageUrl, string caption)
        => SendAsync(phoneNumberId, new WhatsAppImageRequest(to, imageUrl, caption));

    private async Task SendAsync(string phoneNumberId, object request)
    {
        var token = Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN") ?? string.Empty;
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await _http.PostAsJsonAsync($"https://graph.facebook.com/v19.0/{phoneNumberId}/messages", request);
    }
}

public record WhatsAppResponse(string MessagingProduct, List<ContactResponse> Contacts, List<MessageResponse> Messages);
public record ContactResponse(string Input, string WaId);
public record MessageResponse(string Id);

public record WhatsAppTextRequest(string To, string Text)
{
    public string Messaging_Product => "whatsapp";
    public string Type => "text";
}

public record WhatsAppButtonsRequest(string To, string Body, List<(string Id, string Title)> Buttons)
{
    public string Messaging_Product => "whatsapp";
    public string Type => "interactive";
}

public record WhatsAppListRequest(string To, string Body, string ButtonLabel, List<(string Id, string Title, string Description)> Rows)
{
    public string Messaging_Product => "whatsapp";
    public string Type => "interactive";
}

public record WhatsAppImageRequest(string To, string ImageUrl, string Caption)
{
    public string Messaging_Product => "whatsapp";
    public string Type => "image";
}
