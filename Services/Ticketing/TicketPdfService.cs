using BusTicketing.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BusTicketing.Services.Ticketing;

public sealed class TicketPdfService(SettingsService settings, IAppClock clock)
{
    private static readonly QuestPDF.Infrastructure.Color Ink = QuestPDF.Infrastructure.Color.FromHex("#12201C");
    private static readonly QuestPDF.Infrastructure.Color Muted = QuestPDF.Infrastructure.Color.FromHex("#5C6B65");
    private static readonly QuestPDF.Infrastructure.Color Brand = QuestPDF.Infrastructure.Color.FromHex("#0D5C46");
    private static readonly QuestPDF.Infrastructure.Color Line = QuestPDF.Infrastructure.Color.FromHex("#E3E8E5");
    private static readonly QuestPDF.Infrastructure.Color Sunken = QuestPDF.Infrastructure.Color.FromHex("#F4F7F5");

    public byte[] Generate(Booking booking, string verifyUrl)
    {
        var site = settings.Current.SiteName;
        var qr = QrCodeGenerator.Png(verifyUrl);
        var trip = booking.Trip;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Family).FontSize(10).FontColor(Ink));

                page.Header().Element(c => Header(c, site));
                page.Content().PaddingTop(14).Element(c => Body(c, booking, trip, qr));
                page.Footer().PaddingTop(10).Element(c => Footer(c, site));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, string site)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(site).FontSize(18).Bold().FontColor(Brand);
                col.Item().Text("Bus e-ticket").FontSize(9).FontColor(Muted);
            });
            row.ConstantItem(90).AlignRight().Text("PASSENGER COPY").FontSize(8).FontColor(Muted);
        });
    }

    private void Body(IContainer container, Booking b, Trip trip, byte[] qr)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Background(Brand).Padding(12).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"{trip.Route.OriginLocation.Name}  →  {trip.Route.DestinationLocation.Name}")
                        .FontColor(Colors.White).FontSize(15).Bold();
                    c.Item().PaddingTop(2).Text(clock.ToLocal(trip.DepartureTime).ToString("dddd, d MMMM yyyy · HH:mm"))
                        .FontColor(Colors.White).FontSize(10);
                });
                row.ConstantItem(64).Image(qr);
            });

            col.Item().Border(1).BorderColor(Line).CornerRadius(4).Padding(10).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("REFERENCE  ").FontSize(8).FontColor(Muted);
                    t.Span(b.Reference).FontSize(14).Bold();
                });
                row.ConstantItem(90).AlignRight().Column(c =>
                {
                    c.Item().Text("SEATS").FontSize(8).FontColor(Muted).AlignRight();
                    c.Item().Text(b.SeatSummary).FontSize(13).Bold().AlignRight();
                });
            });

            col.Item().Element(c => Grid(c, [
                ("Passenger", b.PassengerName),
                ("Mobile", b.PassengerPhone),
                ("Bus", $"{trip.Bus.Name} · {trip.Bus.Operator}"),
                ("Class", trip.Bus.ClassLabel),
                ("Boarding point", b.BoardingCounter?.Name ?? "—"),
                ("Dropping point", b.DroppingCounter?.Name ?? "—"),
                ("Arrival (est.)", clock.ToLocal(trip.ArrivalTime).ToString("d MMM, HH:mm")),
                ("Fare", $"৳{b.UnitFare:0} × {b.SeatCount} = ৳{b.TotalAmount:0}")
            ]));

            col.Item().Background(Sunken).CornerRadius(4).Padding(10).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Payment  ").FontSize(9).FontColor(Muted);
                    t.Span(PaymentLabel(b)).FontSize(10).Bold();
                });
                row.ConstantItem(120).AlignRight().Text(PayStatusLabel(b.PaymentStatus))
                    .FontSize(10).Bold().FontColor(PayStatusColor(b.PaymentStatus));
            });

            col.Item().PaddingTop(4).Text(
                "Please arrive at the boarding point at least 20 minutes before departure. " +
                "Show this ticket (printed or on your phone) and a valid photo ID when boarding.")
                .FontSize(8).FontColor(Muted).LineHeight(1.4f);
        });
    }

    private static void Grid(IContainer container, (string Label, string Value)[] rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
            });

            foreach (var (label, value) in rows)
            {
                table.Cell().PaddingVertical(3).Text(label).FontSize(8).FontColor(Muted);
                table.Cell().PaddingVertical(3).AlignRight().Text(value).FontSize(10);
            }
        });
    }

    private static void Footer(IContainer container, string site)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"{site} · issued {DateTimeOffset.UtcNow:yyyy-MM-dd}")
                .FontSize(7).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(x =>
            {
                x.CurrentPageNumber().FontSize(7).FontColor(Muted);
            });
        });
    }

    private static string PaymentLabel(Booking b) => b.PaymentMode switch
    {
        PaymentMode.Online => $"Online · {b.Payment?.Provider}",
        _ => "Pay at counter"
    };

    private static string PayStatusLabel(PaymentStatus s) => s switch
    {
        PaymentStatus.Verified => "PAID",
        PaymentStatus.Rejected => "NOT VERIFIED",
        PaymentStatus.Refunded => "REFUNDED",
        _ => "PENDING"
    };

    private static QuestPDF.Infrastructure.Color PayStatusColor(PaymentStatus s) => s switch
    {
        PaymentStatus.Verified => Brand,
        PaymentStatus.Rejected => QuestPDF.Infrastructure.Color.FromHex("#D1453B"),
        _ => QuestPDF.Infrastructure.Color.FromHex("#D9820A")
    };
}
