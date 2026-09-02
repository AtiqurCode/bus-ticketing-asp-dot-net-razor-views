namespace BusTicketing.Services.Notifications;

/// <summary>
/// Default sender while no SMS gateway account is configured. It never
/// pretends to deliver anything — it logs the message and records honestly
/// that nothing went out, so the send-and-log pipeline is fully wired and
/// visible in the audit trail the day real credentials are dropped in.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<SmsSendResult> SendAsync(string toPhone, string message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS (no gateway configured) → {Phone}: {Message}", toPhone, message);
        return Task.FromResult(SmsSendResult.Fail("No SMS gateway configured — logged only."));
    }
}
