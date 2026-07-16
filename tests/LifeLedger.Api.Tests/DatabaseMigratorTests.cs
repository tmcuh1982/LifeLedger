using LifeLedger.Api.Data;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies safe adoption of SQLite databases created before migrations were introduced.</summary>
public sealed class DatabaseMigratorTests
{
    /// <summary>Ensures legacy databases receive every post-baseline migration without rebuilding their original schema.</summary>
    [Fact]
    public async Task Existing_sqlite_database_is_adopted_without_recreating_its_schema()
    {
        // A unique temporary database keeps this migration test independent of the user's local plan.
        var databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-migration-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LifeLedgerDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using (var legacyDatabase = new LifeLedgerDbContext(options))
            {
                // Reproduce the old pre-migrations database format, including only the original table set.
                await legacyDatabase.Database.MigrateAsync("20260716163315_InitialCreate");
                await legacyDatabase.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"");
            }

            await using (var database = new LifeLedgerDbContext(options))
            {
                var migrator = new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance);
                await migrator.ApplyAsync();

                Assert.Equal(
                    database.Database.GetMigrations(),
                    database.Database.GetAppliedMigrations());
                Assert.True(await database.Database.CanConnectAsync());
            }
        }
        finally
        {
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}-shm");
            DeleteIfExists($"{databasePath}-wal");
        }
    }

    /// <summary>Initialises a version marker for databases created before business-data versioning existed.</summary>
    [Fact]
    public async Task Database_without_data_schema_version_is_initialised_at_the_current_version()
    {
        // A separate file keeps the versioning service test independent of migration history behaviour.
        var databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-data-version-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LifeLedgerDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using var database = new LifeLedgerDbContext(options);
            await database.Database.EnsureCreatedAsync();
            var service = new DataSchemaMigrationService(
                database,
                Array.Empty<IDataSchemaMigration>(),
                NullLogger<DataSchemaMigrationService>.Instance);

            await service.EnsureCurrentAsync();

            var setting = await database.ApplicationSettings.FindAsync(DataSchemaMigrationService.DataSchemaVersionKey);
            Assert.NotNull(setting);
            Assert.Equal(DataSchemaMigrationService.LatestVersion.ToString(), setting.Value);
        }
        finally
        {
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}-shm");
            DeleteIfExists($"{databasePath}-wal");
        }
    }

    /// <summary>Removes a temporary SQLite file when it was created by the test.</summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
