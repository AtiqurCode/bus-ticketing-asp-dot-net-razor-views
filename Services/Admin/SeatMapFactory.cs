using BusTicketing.Domain;

namespace BusTicketing.Services.Admin;

/// <summary>
/// Builds a starting seat plan from a simple description — "2+2 across, 11 rows,
/// a full bench at the back". The admin then tweaks individual cells.
/// </summary>
public static class SeatMapFactory
{
    private const string Letters = "ABCDEFGHJKLMNPQRST";

    public static SeatMap Standard(int leftSeats, int rightSeats, int rows, bool backRowFull = true)
    {
        leftSeats = Math.Clamp(leftSeats, 1, 3);
        rightSeats = Math.Clamp(rightSeats, 1, 3);
        rows = Math.Clamp(rows, 1, 20);

        var aisleColumn = leftSeats;
        var columns = leftSeats + 1 + rightSeats;
        var map = new SeatMap { Rows = rows, Columns = columns, Decks = 1 };

        for (var row = 0; row < rows; row++)
        {
            var letter = LetterFor(row);
            var isBackBench = backRowFull && row == rows - 1;
            var seatInRow = 0;

            for (var col = 0; col < columns; col++)
            {
                if (col == aisleColumn && !isBackBench)
                    continue; // leave the aisle empty

                seatInRow++;
                map.Seats.Add(new SeatCell
                {
                    Number = $"{letter}{seatInRow}",
                    Row = row,
                    Column = col,
                    Deck = 1,
                    Type = col == 0 || col == columns - 1 ? SeatType.Window : SeatType.Regular
                });
            }
        }

        return map;
    }

    public static char LetterFor(int rowIndex) =>
        rowIndex < Letters.Length ? Letters[rowIndex] : (char)('A' + rowIndex);
}
