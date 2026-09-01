namespace BusTicketing.Domain;

/// <summary>
/// The money side of a booking. One row per booking. For online payments the
/// passenger submits a transaction id that staff later verify; for counter sales
/// staff record cash or an mFS reference on the spot.
/// </summary>
public class Payment : Entity
{
    public Guid BookingId { get; set; }

    public Booking Booking { get; set; } = null!;

    public PaymentMode Mode { get; set; }

    public MfsProvider? Provider { get; set; }

    /// <summary>The reference the passenger reads off their mFS confirmation.</summary>
    public string? TransactionId { get; set; }

    /// <summary>The wallet number the payment was sent from, if given.</summary>
    public string? SenderMsisdn { get; set; }

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid? ReviewedByStaffId { get; set; }

    public StaffUser? ReviewedByStaff { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }
}
