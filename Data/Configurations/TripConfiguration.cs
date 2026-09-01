using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.Property(t => t.Fare).HasPrecision(10, 2);

        // A uint + IsRowVersion is recognised by Npgsql's model convention and
        // bound to the system xmin column — no real column, no migration noise.
        builder.Property(t => t.Version).IsRowVersion();

        builder.HasOne(t => t.Route)
            .WithMany(r => r.Trips)
            .HasForeignKey(t => t.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Bus)
            .WithMany(b => b.Trips)
            .HasForeignKey(t => t.BusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ScheduleTemplate)
            .WithMany(s => s.Trips)
            .HasForeignKey(t => t.ScheduleTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.BoardingCounter)
            .WithMany()
            .HasForeignKey(t => t.BoardingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.DroppingCounter)
            .WithMany()
            .HasForeignKey(t => t.DroppingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.RouteId, t.DepartureTime });
        builder.HasIndex(t => t.ServiceDate);
        builder.HasIndex(t => t.Status);

        // One generated trip per template per departure instant.
        builder.HasIndex(t => new { t.ScheduleTemplateId, t.DepartureTime })
            .IsUnique()
            .HasFilter("schedule_template_id IS NOT NULL");
    }
}

public class TripSeatConfiguration : IEntityTypeConfiguration<TripSeat>
{
    public void Configure(EntityTypeBuilder<TripSeat> builder)
    {
        builder.Property(s => s.SeatNumber).HasMaxLength(8).IsRequired();

        builder.Property(s => s.Version).IsRowVersion();

        builder.HasOne(s => s.Trip)
            .WithMany(t => t.Seats)
            .HasForeignKey(s => s.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Booking)
            .WithMany()
            .HasForeignKey(s => s.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.TripId, s.SeatNumber }).IsUnique();
        builder.HasIndex(s => new { s.Status, s.HoldExpiresAt });
    }
}
