using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Channels;

namespace VoxAngelos.Services;

public sealed record QueuedEmail(string Recipient, string Subject, string HtmlMessage);

public interface IBackgroundEmailQueue
{
    void Enqueue(string recipient, string subject, string htmlMessage);
}

public sealed class BackgroundEmailQueue : BackgroundService, IBackgroundEmailQueue
{
    private readonly Channel<QueuedEmail> _queue = Channel.CreateUnbounded<QueuedEmail>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundEmailQueue> _logger;

    public BackgroundEmailQueue(IServiceScopeFactory scopeFactory, ILogger<BackgroundEmailQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Enqueue(string recipient, string subject, string htmlMessage) =>
        _queue.Writer.TryWrite(new QueuedEmail(recipient, subject, htmlMessage));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendEmailAsync(email.Recipient, email.Subject, email.HtmlMessage);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A queued notification email could not be delivered to {Recipient}.", email.Recipient);
            }
        }
    }
}
