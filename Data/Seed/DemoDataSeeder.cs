using BusTicketing.Domain;
using BusTicketing.Services.Admin;
using BusTicketing.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Data.Seed;

/// <summary>
/// Optional sample fleet, routes and recurring schedules so a fresh database is
/// immediately testable — a handful of real Bangladesh corridors with buses of
/// every class, plus a week of generated trips.
///
/// Enabled by <c>Seed:DemoData=true</c> or the <c>seed-demo</c> command-line
/// argument. Idempotent: does nothing once a bus exists.
/// </summary>
public static class DemoDataSeeder
{
    private sealed record BusSpec(
        string Name, string Operator, BusClass Class, int Left, int Right, int Rows, string Amenities);

    private sealed record RouteSpec(string Origin, string Destination, int DistanceKm, int DurationMinutes);

    private sealed record ScheduleSpec(
        string Name, string Origin, string Destination, string Bus,
        TimeOnly Start, TimeOnly End, int IntervalMinutes, decimal Fare);

    private static readonly BusSpec[] BusSpecs =
    [
        new("Green Line Volvo B11R",  "Green Line Paribahan", BusClass.AcBusiness, 2, 1, 11, "Wi-Fi, blanket, water, USB charging"),
        new("Hanif Scania Metrolink", "Hanif Enterprise",     BusClass.Ac,         2, 2, 11, "Water, USB charging"),
        new("Shyamoli Ashok Leyland", "Shyamoli Paribahan",   BusClass.NonAc,      2, 2, 12, "Reading light"),
        new("Ena Sleeper Coach",      "Ena Transport",        BusClass.AcSleeper,  2, 1, 10, "Flat-bed berths, blanket, water"),
    ];

    private static readonly RouteSpec[] RouteSpecs =
    [
        new("Dhaka", "Chattogram",  250, 360),
        new("Chattogram", "Dhaka",  250, 360),
        new("Dhaka", "Sylhet",      240, 330),
        new("Sylhet", "Dhaka",      240, 330),
        new("Dhaka", "Khulna",      270, 420),
        new("Dhaka", "Rajshahi",    260, 390),
        new("Dhaka", "Cox's Bazar", 400, 600),
        new("Dhaka", "Barishal",    170, 300),
    ];

    private static readonly ScheduleSpec[] ScheduleSpecs =
    [
        new("Dhaka–Chattogram daytime", "Dhaka", "Chattogram",  "Green Line Volvo B11R",  new(7, 0),  new(23, 0), 120,  850m),
        new("Chattogram–Dhaka daytime", "Chattogram", "Dhaka",  "Green Line Volvo B11R",  new(7, 0),  new(23, 0), 120,  850m),
        new("Dhaka–Sylhet",             "Dhaka", "Sylhet",      "Hanif Scania Metrolink", new(8, 0),  new(22, 0), 180,  700m),
        new("Dhaka–Khulna",             "Dhaka", "Khulna",      "Shyamoli Ashok Leyland", new(9, 0),  new(21, 0), 240,  750m),
        new("Dhaka–Rajshahi",           "Dhaka", "Rajshahi",    "Hanif Scania Metrolink", new(8, 30), new(20, 30), 240, 650m),
        new("Dhaka–Cox's Bazar night",  "Dhaka", "Cox's Bazar", "Ena Sleeper Coach",      new(20, 0), new(23, 0),  60, 1600m),
        new("Dhaka–Barishal",           "Dhaka", "Barishal",    "Shyamoli Ashok Leyland", new(7, 30), new(21, 30), 180, 550m),
    ];

    public static async Task SeedAsync(
        AppDbContext db, TripGenerationService generator, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Buses.AnyAsync(ct))
        {
            logger.LogInformation("Demo data: a bus already exists — nothing to seed.");
            return;
        }

        var cities = await db.Locations
            .Where(l => l.Type == LocationType.City)
            .ToDictionaryAsync(l => l.Name, ct);

        if (cities.Count == 0)
        {
            logger.LogWarning("Demo data: no locations are seeded yet — skipping.");
            return;
        }

        var buses = new Dictionary<string, Bus>();
        foreach (var spec in BusSpecs)
        {
            var map = SeatMapFactory.Standard(spec.Left, spec.Right, spec.Rows);
            var bus = new Bus
            {
                Name = spec.Name,
                Operator = spec.Operator,
                Class = spec.Class,
                SeatMap = map,
                TotalSeats = map.SeatCount,
                Amenities = spec.Amenities,
                IsActive = true,
            };
            buses[spec.Name] = bus;
            db.Buses.Add(bus);
        }

        var routes = new Dictionary<(string From, string To), BusRoute>();
        foreach (var spec in RouteSpecs)
        {
            if (!cities.TryGetValue(spec.Origin, out var origin) ||
                !cities.TryGetValue(spec.Destination, out var destination))
            {
                logger.LogWarning("Demo data: skipping route {From} → {To} (a city is missing).",
                    spec.Origin, spec.Destination);
                continue;
            }

            var route = new BusRoute
            {
                OriginLocationId = origin.Id,
                DestinationLocationId = destination.Id,
                DistanceKm = spec.DistanceKm,
                ApproxDurationMinutes = spec.DurationMinutes,
                IsActive = true,
            };
            routes[(spec.Origin, spec.Destination)] = route;
            db.Routes.Add(route);
        }

        var templates = 0;
        foreach (var spec in ScheduleSpecs)
        {
            if (!routes.TryGetValue((spec.Origin, spec.Destination), out var route) ||
                !buses.TryGetValue(spec.Bus, out var bus))
                continue;

            db.ScheduleTemplates.Add(new ScheduleTemplate
            {
                Name = spec.Name,
                Route = route,
                Bus = bus,
                StartTime = spec.Start,
                EndTime = spec.End,
                IntervalMinutes = spec.IntervalMinutes,
                Fare = spec.Fare,
                OperatingDays = WeekDays.All,
                IsActive = true,
            });
            templates++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo data: {Buses} buses, {Routes} routes, {Templates} schedules.",
            buses.Count, routes.Count, templates);

        // Materialise trips right away so the app is bookable without waiting for
        // the background generator's first pass.
        var summary = await generator.TopUpAsync(ct);
        logger.LogInformation("Demo data: generated {Trips} trips ({Seats} seats) across the booking window.",
            summary.TripsCreated, summary.SeatsCreated);
    }
}
