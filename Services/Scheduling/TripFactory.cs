using BusTicketing.Domain;

namespace BusTicketing.Services.Scheduling;

/// <summary>Builds a <see cref="Trip"/> with its seat rows from a bus's plan.</summary>
public static class TripFactory
{
    public static Trip Create(
        BusRoute route, Bus bus, DateTimeOffset departure, decimal fare,
        Guid? scheduleTemplateId, bool manual,
        Guid? boardingCounterId, Guid? droppingCounterId, DateOnly serviceDate)
    {
        var trip = new Trip
        {
            RouteId = route.Id,
            BusId = bus.Id,
            ScheduleTemplateId = scheduleTemplateId,
            ServiceDate = serviceDate,
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(route.ApproxDurationMinutes),
            Fare = fare,
            Status = TripStatus.Scheduled,
            IsManualOverride = manual,
            BoardingCounterId = boardingCounterId,
            DroppingCounterId = droppingCounterId
        };

        foreach (var seat in bus.SeatMap.Seats)
        {
            trip.Seats.Add(new TripSeat
            {
                SeatNumber = seat.Number,
                SeatType = seat.Type,
                Status = SeatStatus.Available
            });
        }

        return trip;
    }
}
