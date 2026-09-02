using BusTicketing.Services.Bookings;

namespace BusTicketing.Services.Ticketing;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tickets/{reference}.pdf", async (
            string reference, HttpRequest request, BookingService bookings, TicketPdfService pdf) =>
        {
            var booking = await bookings.GetByReferenceAsync(reference);
            if (booking is null)
                return Results.NotFound();

            var verifyUrl = $"{request.Scheme}://{request.Host}/booking/{booking.Reference}";
            var bytes = pdf.Generate(booking, verifyUrl);
            return Results.File(bytes, "application/pdf", $"{booking.Reference}.pdf");
        }).AllowAnonymous().ExcludeFromDescription();

        return endpoints;
    }
}
