using System.Text.Json;
using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Data.Seed;

/// <summary>
/// Loads the curated Bangladesh location list from <c>bangladesh-locations.json</c>:
/// every district as a City, plus the hand-picked terminals hanging off the big hubs.
/// Runs once — if any location already exists it does nothing.
/// </summary>
public static class LocationSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Locations.AnyAsync(ct))
            return;

        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "bangladesh-locations.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Location seed file not found at {Path}; skipping location seed.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<SeedRoot>(stream, JsonOpts, ct)
            ?? throw new InvalidOperationException("Could not parse bangladesh-locations.json");

        var toAdd = new List<Location>();

        foreach (var division in payload.Divisions)
        {
            foreach (var district in division.Districts)
            {
                var city = new Location
                {
                    Division = division.Name,
                    District = district.Name,
                    Name = district.Name,
                    NameBn = district.NameBn,
                    Type = LocationType.City,
                    IsActive = true
                };
                toAdd.Add(city);

                foreach (var terminal in district.Terminals ?? [])
                {
                    toAdd.Add(new Location
                    {
                        Division = division.Name,
                        District = district.Name,
                        Name = terminal.Name,
                        NameBn = terminal.NameBn,
                        Type = LocationType.Terminal,
                        Parent = city,
                        IsActive = true
                    });
                }
            }
        }

        db.Locations.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} locations ({Cities} cities).",
            toAdd.Count, toAdd.Count(l => l.Type == LocationType.City));
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record SeedRoot(List<SeedDivision> Divisions);
    private sealed record SeedDivision(string Name, string? NameBn, List<SeedDistrict> Districts);
    private sealed record SeedDistrict(string Name, string? NameBn, List<SeedTerminal>? Terminals);
    private sealed record SeedTerminal(string Name, string? NameBn);
}
