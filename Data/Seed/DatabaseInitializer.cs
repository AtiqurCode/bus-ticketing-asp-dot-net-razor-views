using BusTicketing.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BusTicketing.Data.Seed;

/// <summary>
/// Startup routine: make sure the database exists (as UTF-8), apply migrations,
/// then lay down the reference data the app can't run without.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task RunAsync(IServiceProvider services, IHostEnvironment env, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var config = sp.GetRequiredService<IConfiguration>();

        await EnsureDatabaseExistsAsync(config.GetConnectionString("Postgres")!, logger, ct);

        var db = sp.GetRequiredService<AppDbContext>();
        logger.LogInformation("Applying migrations…");
        await db.Database.MigrateAsync(ct);

        await SeedRolesAsync(sp, ct);
        await SeedSuperAdminAsync(sp, config, logger, ct);
        await SeedSettingsAsync(db, ct);
        await SeedCancellationPolicyAsync(db, ct);
        await LocationSeeder.SeedAsync(db, logger, ct);

        logger.LogInformation("Database ready.");
    }

    /// <summary>
    /// Npgsql would happily <c>CREATE DATABASE</c> during Migrate, but it inherits
    /// the cluster template's WIN1252 encoding — no good for Bangla. So we create
    /// it ourselves with an explicit UTF-8 / C-locale definition first.
    /// </summary>
    private static async Task EnsureDatabaseExistsAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = target.Database
            ?? throw new InvalidOperationException("Connection string has no database name.");

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync(ct) is not null)
                return;
        }

        logger.LogInformation("Creating database {Database} (UTF-8)…", databaseName);
        var quoted = databaseName.Replace("\"", "\"\"");
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{quoted}\" WITH ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0",
            connection);
        await create.ExecuteNonQueryAsync(ct);
    }

    private static async Task SeedRolesAsync(IServiceProvider sp, CancellationToken ct)
    {
        var roleManager = sp.GetRequiredService<RoleManager<StaffRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new StaffRole(role));
        }
    }

    private static async Task SeedSuperAdminAsync(
        IServiceProvider sp, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var userManager = sp.GetRequiredService<UserManager<StaffUser>>();

        var username = config["Seed:SuperAdminUsername"] ?? "admin";
        if (await userManager.FindByNameAsync(username) is not null)
            return;

        var admin = new StaffUser
        {
            UserName = username,
            Email = config["Seed:SuperAdminEmail"],
            EmailConfirmed = true,
            FullName = "System Administrator",
            IsActive = true
        };

        var password = config["Seed:SuperAdminPassword"] ?? "ChangeMe!2026";
        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to create the super admin: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Roles.SuperAdmin);
        logger.LogWarning("Seeded super admin '{Username}' with the configured password — change it after first login.", username);
    }

    private static async Task SeedSettingsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.AppSettings.AnyAsync(ct))
            return;

        db.AppSettings.Add(new AppSettings { Id = AppSettings.SingletonId });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCancellationPolicyAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.CancellationPolicies.AnyAsync(ct))
            return;

        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Name = "Standard",
            IsDefault = true,
            IsActive = true,
            Rules =
            [
                new CancellationRule { MinHoursBeforeDeparture = 24, RefundPercent = 90 },
                new CancellationRule { MinHoursBeforeDeparture = 6, RefundPercent = 50 },
                new CancellationRule { MinHoursBeforeDeparture = 0, RefundPercent = 0 }
            ]
        });
        await db.SaveChangesAsync(ct);
    }
}
