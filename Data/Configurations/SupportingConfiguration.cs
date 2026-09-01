using BusTicketing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusTicketing.Data.Configurations;

public class CancellationPolicyConfiguration : IEntityTypeConfiguration<CancellationPolicy>
{
    public void Configure(EntityTypeBuilder<CancellationPolicy> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();

        builder.HasMany(p => p.Rules)
            .WithOne(r => r.Policy)
            .HasForeignKey(r => r.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Only one row may carry IsDefault = true.
        builder.HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasFilter("is_default");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.ActorName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(60);
        builder.Property(a => a.Summary).HasMaxLength(400).IsRequired();
        builder.Property(a => a.DetailJson).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(45);

        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.ActorUserId);
    }
}

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.IntervalOptionsCsv).HasMaxLength(120);
        builder.Property(s => s.TimeZoneId).HasMaxLength(60);
        builder.Property(s => s.CurrencyCode).HasMaxLength(3);
        builder.Property(s => s.BookingReferencePrefix).HasMaxLength(6);
        builder.Property(s => s.SiteName).HasMaxLength(80);
        builder.Property(s => s.SupportPhone).HasMaxLength(20);

        builder.ToTable(t => t.HasCheckConstraint("ck_app_settings_singleton", "id = 1"));
    }
}

public class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(120).IsRequired();

        builder.HasOne(u => u.CounterLocation)
            .WithMany()
            .HasForeignKey(u => u.CounterLocationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
