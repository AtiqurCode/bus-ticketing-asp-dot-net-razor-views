namespace BusTicketing.Domain;

/// <summary>
/// A recurring departure pattern for a route. The trip generator walks each
/// active template and materialises real <see cref="Trip"/> rows across the
/// rolling booking window.
/// </summary>
public class ScheduleTemplate : Entity
{
    public string Name { get; set; } = "";

    public Guid RouteId { get; set; }

    public BusRoute Route { get; set; } = null!;

    public Guid BusId { get; set; }

    public Bus Bus { get; set; } = null!;

    /// <summary>First departure of the operating day, in the platform time zone.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Latest departure of the operating day. Departures stop once the
    /// running clock passes this time.</summary>
    public TimeOnly EndTime { get; set; }

    public int IntervalMinutes { get; set; }

    public decimal Fare { get; set; }

    public WeekDays OperatingDays { get; set; } = WeekDays.All;

    /// <summary>Optional default boarding / dropping points offered to the passenger.</summary>
    public Guid? BoardingCounterId { get; set; }

    public Location? BoardingCounter { get; set; }

    public Guid? DroppingCounterId { get; set; }

    public Location? DroppingCounter { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Trip> Trips { get; set; } = [];

    /// <summary>Departure times this template implies for a single service day.</summary>
    public IEnumerable<TimeOnly> DepartureTimesOfDay()
    {
        if (IntervalMinutes <= 0)
        {
            yield return StartTime;
            yield break;
        }

        for (var t = StartTime; t <= EndTime; t = t.AddMinutes(IntervalMinutes))
        {
            yield return t;
            // Guard against wrapping past midnight into an infinite loop.
            if (t.AddMinutes(IntervalMinutes) < t) yield break;
        }
    }
}
