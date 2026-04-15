using Citapp.BotApi.Infrastructure.Repositories;

namespace Citapp.BotApi.Services;

public sealed class ConversationStateCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationStateCleanupService> _logger;

    public ConversationStateCleanupService(IServiceScopeFactory scopeFactory, ILogger<ConversationStateCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Conversation state cleanup failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var states = scope.ServiceProvider.GetRequiredService<ConversationStateRepository>();
        var deleted = await states.DeleteExpiredAsync(DateTimeOffset.UtcNow);
        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {deletedCount} expired conversation states", deleted);
        }

        stoppingToken.ThrowIfCancellationRequested();
    }
}
