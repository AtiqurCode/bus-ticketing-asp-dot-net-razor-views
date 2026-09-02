using BusTicketing.Domain;

namespace BusTicketing.Services.Notifications;

/// <summary>Builds the SMS copy for each booking milestone.</summary>
public static class TicketMessages
{
    public static string BookingReceived(Booking booking, Trip trip, IAppClock clock, string? link)
    {
        var when = clock.ToLocal(trip.DepartureTime).ToString("d MMM, HH:mm");
        var tail = link is null ? "" : $" Ticket: {link}";
        return booking.PaymentMode == PaymentMode.Online
            ? $"{booking.Reference}: seat(s) {booking.SeatSummary} for {trip.Route.OriginLocation.Name}-{trip.Route.DestinationLocation.Name} on {when} held. We're checking your payment.{tail}"
            : $"{booking.Reference}: seat(s) {booking.SeatSummary} for {trip.Route.OriginLocation.Name}-{trip.Route.DestinationLocation.Name} on {when} reserved. Pay at any counter to confirm.{tail}";
    }

    public static string PaymentConfirmed(Booking booking, Trip trip, IAppClock clock, string? link)
    {
        var when = clock.ToLocal(trip.DepartureTime).ToString("d MMM, HH:mm");
        var tail = link is null ? "" : $" Ticket: {link}";
        return $"{booking.Reference} CONFIRMED. Seat(s) {booking.SeatSummary}, {trip.Route.OriginLocation.Name}-{trip.Route.DestinationLocation.Name}, {when}. Have a safe trip!{tail}";
    }

    public static string PaymentRejected(Booking booking, string? note) =>
        $"{booking.Reference}: we couldn't verify your payment"
            + (string.IsNullOrWhiteSpace(note) ? "." : $" ({note}).")
            + " Please resubmit your transaction ID before the hold expires.";

    public static string BookingCancelled(Booking booking, string? reason) =>
        $"{booking.Reference} has been cancelled"
            + (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}.");

    public static string? BuildLink(string? publicBaseUrl, string reference) =>
        string.IsNullOrWhiteSpace(publicBaseUrl) ? null : $"{publicBaseUrl.TrimEnd('/')}/booking/{reference}";
}
