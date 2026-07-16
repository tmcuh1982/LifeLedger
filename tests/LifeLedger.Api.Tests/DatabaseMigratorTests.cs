using LifeLedger.Api.Data;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies safe adoption of SQLite databases created before migrations were introduced.</summary>
public sealed class DatabaseMigratorTests
{
    /// <summary>Ensures baselining records migration history without dropping or rebuilding an existing schema.</summary>
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
                await legacyDatabase.Database.EnsureCreatedAsync();
            }

            await using (var database = new LifeLedgerDbContext(options))
            {
                var migrator = new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance);
                await migrator.ApplyAsync();

                Assert.Contains("20260716163315_InitialCreate", database.Database.GetAppliedMigrations());
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

    /// <summary>Removes a temporary SQLite file when it was created by the test.</summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
