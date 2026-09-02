namespace BusTicketing.Domain;

/// <summary>
/// Record of every SMS the platform has tried to send. Doubles as the audit
/// trail while no real gateway is wired up — <see cref="Sent"/> stays false and
/// <see cref="ProviderResponse"/> explains why.
/// </summary>
public class SmsLog : Entity
{
    public string ToPhone { get; set; } = "";

    public string Message { get; set; } = "";

    /// <summary>Slug like "booking.created" or "payment.verified".</summary>
    public string Purpose { get; set; } = "";

    public Guid? BookingId { get; set; }

    public bool Sent { get; set; }

    public string? ProviderResponse { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
