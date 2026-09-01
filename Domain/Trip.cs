namespace BusTicketing.Domain;

/// <summary>
/// A concrete, bookable departure on a date. Either generated from a
/// <see cref="ScheduleTemplate"/> or added by hand (<see cref="IsManualOverride"/>).
/// </summary>
public class Trip : Entity
{
    public Guid RouteId { get; set; }

    public BusRoute Route { get; set; } = null!;

    public Guid BusId { get; set; }

    public Bus Bus { get; set; } = null!;

    public Guid? ScheduleTemplateId { get; set; }

    public ScheduleTemplate? ScheduleTemplate { get; set; }

    /// <summary>Service day in the platform time zone — the dedup key with the template.</summary>
    public DateOnly ServiceDate { get; set; }

    public DateTimeOffset DepartureTime { get; set; }

    public DateTimeOffset ArrivalTime { get; set; }

    public decimal Fare { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    /// <summary>
    /// When true the generator will neither update nor delete this trip — it was
    /// created or edited by an admin, or is a one-off outside any template.
    /// </summary>
    public bool IsManualOverride { get; set; }

    public Guid? BoardingCounterId { get; set; }

    public Location? BoardingCounter { get; set; }

    public Guid? DroppingCounterId { get; set; }

    public Location? DroppingCounter { get; set; }

    public ICollection<TripSeat> Seats { get; set; } = [];

    public ICollection<Booking> Bookings { get; set; } = [];

    /// <summary>Maps to Postgres <c>xmin</c> — bumps on every write for optimistic concurrency.</summary>
    public uint Version { get; private set; }

    public bool IsBookable =>
        Status == TripStatus.Scheduled && DepartureTime > DateTimeOffset.UtcNow;
}
