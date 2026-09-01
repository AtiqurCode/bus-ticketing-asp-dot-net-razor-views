namespace BusTicketing.Domain;

/// <summary>
/// A directed origin → destination pair that trips run along. Named
/// <c>BusRoute</c> rather than <c>Route</c> to stay clear of the Blazor routing type.
/// </summary>
public class BusRoute : Entity
{
    public Guid OriginLocationId { get; set; }

    public Location OriginLocation { get; set; } = null!;

    public Guid DestinationLocationId { get; set; }

    public Location DestinationLocation { get; set; } = null!;

    public decimal DistanceKm { get; set; }

    public int ApproxDurationMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ScheduleTemplate> ScheduleTemplates { get; set; } = [];

    public ICollection<Trip> Trips { get; set; } = [];

    public TimeSpan ApproxDuration => TimeSpan.FromMinutes(ApproxDurationMinutes);
}
