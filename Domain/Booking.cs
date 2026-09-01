namespace BusTicketing.Domain;

public class Booking : Entity
{
    /// <summary>Short human-friendly code printed on the ticket — "BT-7Q3K9F".</summary>
    public string Reference { get; set; } = "";

    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = null!;

    public string PassengerName { get; set; } = "";

    /// <summary>Normalised to local digits ("01712345678") — the key "My Tickets" looks up.</summary>
    public string PassengerPhone { get; set; } = "";

    public string? PassengerEmail { get; set; }

    public List<BookingSeat> Seats { get; set; } = [];

    public decimal UnitFare { get; set; }

    public int SeatCount { get; set; }

    public decimal TotalAmount { get; set; }

    public PaymentMode PaymentMode { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public BookingStatus Status { get; set; } = BookingStatus.Reserved;

    public Payment? Payment { get; set; }

    public Guid? BoardingCounterId { get; set; }

    public Location? BoardingCounter { get; set; }

    public Guid? DroppingCounterId { get; set; }

    public Location? DroppingCounter { get; set; }

    /// <summary>Null when the passenger booked it themselves; set for counter sales.</summary>
    public Guid? BookedByStaffId { get; set; }

    public StaffUser? BookedByStaff { get; set; }

    /// <summary>While <see cref="BookingStatus.Reserved"/>, the moment the hold lapses.</summary>
    public DateTimeOffset? HoldExpiresAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public Guid? CancelledByStaffId { get; set; }

    public decimal? RefundAmount { get; set; }

    public string? Notes { get; set; }

    public string SeatSummary => string.Join(", ", Seats.Select(s => s.SeatNumber));
}

public class BookingSeat
{
    public Guid BookingId { get; set; }

    public Booking Booking { get; set; } = null!;

    public string SeatNumber { get; set; } = "";
}
