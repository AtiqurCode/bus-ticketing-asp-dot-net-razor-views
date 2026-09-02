using BusTicketing.Domain;
using BusTicketing.Services.Scheduling;
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
    public static async Task RunAsync(
        IServiceProvider services, IHostEnvironment env,
        bool includeDemoData = false, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var config = sp.GetRequiredService<IConfiguration>();

        await EnsureDatabaseExistsAsync(config.GetConnectionString("Postgres")!, logger, ct);

        var db = sp.GetRequiredService<AppDbContext>();
        logger.LogInformation("Applying migrations…");
        await db.Database.MigrateAsync(ct);

        await SeedRolesAsync(sp, logger, ct);
        await SeedSuperAdminAsync(sp, config, logger, ct);
        await SeedSettingsAsync(db, ct);
        await SeedCancellationPolicyAsync(db, ct);
        await LocationSeeder.SeedAsync(db, logger, ct);

        if (includeDemoData)
            await DemoDataSeeder.SeedAsync(db, sp.GetRequiredService<TripGenerationService>(), logger, ct);

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

    private static async Task SeedRolesAsync(IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var roleManager = sp.GetRequiredService<RoleManager<StaffRole>>();
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
                continue;

            var result = await roleManager.CreateAsync(new StaffRole(role));
            if (!result.Succeeded)
                logger.LogError("Failed to create role {Role}: {Errors}",
                    role, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Find-or-create the configured super admin and make sure it's actually in
    /// the SuperAdmin role — every boot, not just the first. The role grant is
    /// checked because a silently-failed <c>AddToRoleAsync</c> leaves an account
    /// that can sign in but hits "not allowed" on every screen.
    /// </summary>
    private static async Task SeedSuperAdminAsync(
        IServiceProvider sp, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var userManager = sp.GetRequiredService<UserManager<StaffUser>>();
        var username = config["Seed:SuperAdminUsername"] ?? "admin";

        var admin = await userManager.FindByNameAsync(username);
        if (admin is null)
        {
            admin = new StaffUser
            {
                UserName = username,
                Email = config["Seed:SuperAdminEmail"],
                EmailConfirmed = true,
                FullName = "System Administrator",
                IsActive = true
            };

            var password = config["Seed:SuperAdminPassword"] ?? "ChangeMe!2026";
            var created = await userManager.CreateAsync(admin, password);
            if (!created.Succeeded)
            {
                logger.LogError("Failed to create the super admin: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogWarning("Seeded super admin '{Username}' with the configured password — change it after first login.", username);
        }

        if (await userManager.IsInRoleAsync(admin, Roles.SuperAdmin))
            return;

        var granted = await userManager.AddToRoleAsync(admin, Roles.SuperAdmin);
        if (granted.Succeeded)
            logger.LogInformation("Granted '{Username}' the {Role} role.", username, Roles.SuperAdmin);
        else
            logger.LogError("Could not put '{Username}' in the {Role} role: {Errors}",
                username, Roles.SuperAdmin, string.Join("; ", granted.Errors.Select(e => e.Description)));
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
