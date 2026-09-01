using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.Property(b => b.Reference).HasMaxLength(16).IsRequired();
        builder.Property(b => b.PassengerName).HasMaxLength(120).IsRequired();
        builder.Property(b => b.PassengerPhone).HasMaxLength(20).IsRequired();
        builder.Property(b => b.PassengerEmail).HasMaxLength(160);
        builder.Property(b => b.CancellationReason).HasMaxLength(400);
        builder.Property(b => b.Notes).HasMaxLength(1000);
        builder.Property(b => b.UnitFare).HasPrecision(10, 2);
        builder.Property(b => b.TotalAmount).HasPrecision(10, 2);
        builder.Property(b => b.RefundAmount).HasPrecision(10, 2);

        builder.HasOne(b => b.Trip)
            .WithMany(t => t.Bookings)
            .HasForeignKey(b => b.TripId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(b => b.Seats, seat =>
        {
            seat.WithOwner(s => s.Booking).HasForeignKey(s => s.BookingId);
            seat.Property(s => s.SeatNumber).HasMaxLength(8).IsRequired();
            seat.HasKey(s => new { s.BookingId, s.SeatNumber });
        });

        builder.HasOne(b => b.Payment)
            .WithOne(p => p.Booking)
            .HasForeignKey<Payment>(p => p.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.BookedByStaff)
            .WithMany()
            .HasForeignKey(b => b.BookedByStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(b => b.BoardingCounter)
            .WithMany()
            .HasForeignKey(b => b.BoardingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(b => b.DroppingCounter)
            .WithMany()
            .HasForeignKey(b => b.DroppingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(b => b.Reference).IsUnique();
        builder.HasIndex(b => b.PassengerPhone);
        builder.HasIndex(b => new { b.Status, b.PaymentStatus });
        builder.HasIndex(b => new { b.Status, b.HoldExpiresAt });
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.TransactionId).HasMaxLength(60);
        builder.Property(p => p.SenderMsisdn).HasMaxLength(20);
        builder.Property(p => p.ReviewNote).HasMaxLength(400);
        builder.Property(p => p.Amount).HasPrecision(10, 2);

        builder.HasOne(p => p.ReviewedByStaff)
            .WithMany()
            .HasForeignKey(p => p.ReviewedByStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.BookingId).IsUnique();
        builder.HasIndex(p => p.TransactionId);
        builder.HasIndex(p => new { p.Mode, p.Status });
    }
}
