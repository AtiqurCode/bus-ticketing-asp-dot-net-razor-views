using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class ScheduleTemplateConfiguration : IEntityTypeConfiguration<ScheduleTemplate>
{
    public void Configure(EntityTypeBuilder<ScheduleTemplate> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Fare).HasPrecision(10, 2);

        // Bitmask — keep it an int so it stays queryable and compact.
        builder.Property(t => t.OperatingDays).HasConversion<int>();

        builder.HasOne(t => t.Route)
            .WithMany(r => r.ScheduleTemplates)
            .HasForeignKey(t => t.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Bus)
            .WithMany(b => b.ScheduleTemplates)
            .HasForeignKey(t => t.BusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.BoardingCounter)
            .WithMany()
            .HasForeignKey(t => t.BoardingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.DroppingCounter)
            .WithMany()
            .HasForeignKey(t => t.DroppingCounterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.RouteId, t.IsActive });
    }
}
