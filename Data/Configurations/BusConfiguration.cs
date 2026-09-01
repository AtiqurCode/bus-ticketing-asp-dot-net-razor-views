using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class BusConfiguration : IEntityTypeConfiguration<Bus>
{
    public void Configure(EntityTypeBuilder<Bus> builder)
    {
        builder.Property(b => b.Name).HasMaxLength(120).IsRequired();
        builder.Property(b => b.Operator).HasMaxLength(120).IsRequired();
        builder.Property(b => b.RegistrationNumber).HasMaxLength(40);
        builder.Property(b => b.Amenities).HasMaxLength(400);

        // The seat plan travels as a single jsonb document.
        builder.OwnsOne(b => b.SeatMap, map =>
        {
            map.ToJson();
            map.OwnsMany(m => m.Seats);
        });

        builder.HasIndex(b => b.Operator);
        builder.HasIndex(b => b.IsActive);
    }
}

public class BusRouteConfiguration : IEntityTypeConfiguration<BusRoute>
{
    public void Configure(EntityTypeBuilder<BusRoute> builder)
    {
        builder.Property(r => r.DistanceKm).HasPrecision(7, 2);

        builder.HasOne(r => r.OriginLocation)
            .WithMany()
            .HasForeignKey(r => r.OriginLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DestinationLocation)
            .WithMany()
            .HasForeignKey(r => r.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.OriginLocationId, r.DestinationLocationId, r.IsActive });
    }
}
