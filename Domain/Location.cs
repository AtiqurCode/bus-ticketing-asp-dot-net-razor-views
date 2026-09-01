namespace BusTicketing.Domain;

/// <summary>
/// A point on the map that a route can start or end at. The list is hierarchical:
/// a <see cref="LocationType.Counter"/> or <see cref="LocationType.Terminal"/>
/// usually hangs off a <see cref="LocationType.City"/> via <see cref="ParentLocationId"/>.
/// </summary>
public class Location : Entity
{
    public string Division { get; set; } = "";

    public string District { get; set; } = "";

    /// <summary>City, terminal or counter name — shown in search dropdowns.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional Bangla rendering of <see cref="Name"/> for the localised UI.</summary>
    public string? NameBn { get; set; }

    public LocationType Type { get; set; } = LocationType.City;

    public Guid? ParentLocationId { get; set; }

    public Location? Parent { get; set; }

    public ICollection<Location> Children { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>e.g. "Dhaka — Gabtoli Bus Terminal". Not persisted.</summary>
    public string DisplayName =>
        Parent is null || Parent.Name == Name ? Name : $"{Parent.Name} — {Name}";
}
