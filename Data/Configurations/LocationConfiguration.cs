using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.Property(l => l.Division).HasMaxLength(60).IsRequired();
        builder.Property(l => l.District).HasMaxLength(60).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(120).IsRequired();
        builder.Property(l => l.NameBn).HasMaxLength(120);

        builder.HasOne(l => l.Parent)
            .WithMany(l => l.Children)
            .HasForeignKey(l => l.ParentLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.Name);
        builder.HasIndex(l => new { l.Division, l.District });
        builder.HasIndex(l => new { l.Type, l.IsActive });
    }
}
