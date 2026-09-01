namespace BusTicketing.Domain;

/// <summary>
/// The physical seat plan of a bus, stored as JSON on <see cref="Bus.SeatMap"/>.
/// Rows/Columns describe the grid the admin builder draws on; only cells that
/// actually carry a seat appear in <see cref="Seats"/>. Everything else — aisles,
/// the stairwell, the driver — is just an empty cell.
/// </summary>
public class SeatMap
{
    public int Rows { get; set; }

    public int Columns { get; set; }

    /// <summary>1 for a normal coach, 2 for a double-decker sleeper.</summary>
    public int Decks { get; set; } = 1;

    public List<SeatCell> Seats { get; set; } = [];

    public int SeatCount => Seats.Count;

    public IEnumerable<string> SeatNumbers => Seats.Select(s => s.Number);

    public static SeatMap Empty() => new() { Rows = 0, Columns = 0, Seats = [] };
}

public class SeatCell
{
    /// <summary>Passenger-facing label, unique within the bus — "A1", "B4", "U12".</summary>
    public string Number { get; set; } = "";

    public int Deck { get; set; } = 1;

    public int Row { get; set; }

    public int Column { get; set; }

    public SeatType Type { get; set; } = SeatType.Regular;
}
