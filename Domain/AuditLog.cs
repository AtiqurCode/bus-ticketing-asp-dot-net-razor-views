namespace BusTicketing.Domain;

/// <summary>Append-only record of a consequential admin or staff action.</summary>
public class AuditLog : Entity
{
    public Guid? ActorUserId { get; set; }

    public string ActorName { get; set; } = "";

    /// <summary>Verb-ish slug — "booking.cancel", "payment.verify", "trip.generate".</summary>
    public string Action { get; set; } = "";

    public string EntityType { get; set; } = "";

    public string? EntityId { get; set; }

    public string Summary { get; set; } = "";

    /// <summary>Optional JSON snapshot of what changed.</summary>
    public string? DetailJson { get; set; }

    public string? IpAddress { get; set; }
}

public static class AuditActions
{
    public const string BookingCreate = "booking.create";
    public const string BookingCancel = "booking.cancel";
    public const string BookingReschedule = "booking.reschedule";
    public const string PaymentVerify = "payment.verify";
    public const string PaymentReject = "payment.reject";
    public const string PaymentRefund = "payment.refund";
    public const string TripGenerate = "trip.generate";
    public const string TripOverride = "trip.override";
    public const string TripCancel = "trip.cancel";
    public const string StaffLogin = "staff.login";
    public const string EntityCreate = "entity.create";
    public const string EntityUpdate = "entity.update";
    public const string EntityDelete = "entity.delete";
    public const string SettingsUpdate = "settings.update";
}
