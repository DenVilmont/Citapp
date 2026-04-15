namespace Citapp.BotApi.Services;

public class BotFsmHandler
{
    public Task HandleAsync(Guid tenantId, Guid customerId, string waUserId, string phoneNumberId, string messageType, string? interactiveType, string? payloadId, string? textBody)
    {
        return Task.CompletedTask;
    }
}
