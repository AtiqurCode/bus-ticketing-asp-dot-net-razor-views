namespace BusTicketing.Domain;

/// <summary>
/// One seat on one trip. This is the row that gets locked during booking, so it
/// carries its own concurrency token and the transient hold fields.
/// </summary>
public class TripSeat : Entity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = null!;

    public string SeatNumber { get; set; } = "";

    public SeatType SeatType { get; set; } = SeatType.Regular;

    public SeatStatus Status { get; set; } = SeatStatus.Available;

    /// <summary>Identifies the checkout session that currently holds this seat.</summary>
    public Guid? HoldToken { get; set; }

    public DateTimeOffset? HoldExpiresAt { get; set; }

    public Guid? BookingId { get; set; }

    public Booking? Booking { get; set; }

    /// <summary>Maps to Postgres <c>xmin</c> — the lock guarding a seat during booking.</summary>
    public uint Version { get; private set; }

    public bool IsHoldActive(DateTimeOffset now) =>
        Status == SeatStatus.Held && HoldExpiresAt > now;

    public bool IsAvailable(DateTimeOffset now) =>
        Status == SeatStatus.Available ||
        (Status == SeatStatus.Held && HoldExpiresAt <= now);
}
