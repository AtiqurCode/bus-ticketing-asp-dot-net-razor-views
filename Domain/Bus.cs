namespace BusTicketing.Domain;

public class Bus : Entity
{
    public string Name { get; set; } = "";

    /// <summary>Operating company — "Hanif Enterprise", "Shyamoli Paribahan".</summary>
    public string Operator { get; set; } = "";

    public string? RegistrationNumber { get; set; }

    public BusClass Class { get; set; } = BusClass.NonAc;

    /// <summary>Denormalised from <see cref="SeatMap"/> so trip queries don't deserialise JSON.</summary>
    public int TotalSeats { get; set; }

    public SeatMap SeatMap { get; set; } = SeatMap.Empty();

    public string? Amenities { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ScheduleTemplate> ScheduleTemplates { get; set; } = [];

    public ICollection<Trip> Trips { get; set; } = [];

    public string ClassLabel => Class switch
    {
        BusClass.NonAc => "Non-AC",
        BusClass.Ac => "AC",
        BusClass.AcSleeper => "AC Sleeper",
        BusClass.AcBusiness => "AC Business",
        _ => Class.ToString()
    };
}
