using BusTicketing.Data;
using Microsoft.EntityFrameworkCore;

namespace BusTicketing.Services;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapGet("/healthz/ready", async (IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var canConnect = await db.Database.CanConnectAsync(ct);
            return canConnect
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "degraded" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous().ExcludeFromDescription();

        return endpoints;
    }
}
