namespace BusTicketing.Services.Notifications;

public sealed record SmsSendResult(bool Succeeded, string ProviderResponse)
{
    public static SmsSendResult Ok(string response) => new(true, response);
    public static SmsSendResult Fail(string response) => new(false, response);
}

/// <summary>
/// The one seam a real gateway (BulkSMSBD, Alpha SMS, …) plugs into. Swap the
/// registered implementation in Program.cs once credentials exist — nothing
/// else in the app needs to change.
/// </summary>
public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string toPhone, string message, CancellationToken ct = default);
}
