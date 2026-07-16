using LifeLedger.Api.Data;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLedger.Api.Tests;

public sealed class DatabaseMigratorTests
{
    [Fact]
    public async Task Existing_sqlite_database_is_adopted_without_recreating_its_schema()
    {
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

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
