namespace AiEmailer.Api.Services;

public sealed class MailboxSyncWorker(IServiceScopeFactory scopes, MailProviderSettings providers,
    IConfiguration config, ILogger<MailboxSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!providers.Gmail || !config.GetValue<bool>("Mailbox:SyncEnabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Mailbox:SyncIntervalSeconds", 120), 30, 3600)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IMailboxRepository>();
                var mailbox = scope.ServiceProvider.GetRequiredService<MailboxService>();
                foreach (var owner in await repository.ListOwnersAsync(stoppingToken))
                {
                    try { await mailbox.SyncAsync(owner, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning("Mailbox sync skipped: {ErrorType}", ex.GetType().Name); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Mailbox sync unavailable: {ErrorType}", ex.GetType().Name); }
        }
    }
}
