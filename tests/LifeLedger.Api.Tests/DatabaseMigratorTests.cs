using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
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

    /// <summary>Ensures the version-one business data is upgraded with an initial point for every existing asset.</summary>
    [Fact]
    public async Task Version_one_assets_receive_an_initial_valuation_point()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-valuation-version-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LifeLedgerDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using var database = new LifeLedgerDbContext(options);
            await database.Database.EnsureCreatedAsync();
            var scenario = new FinancialScenario
            {
                Profile = new Profile { DisplayName = "Migration profile" },
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Assets =
                [
                    new Asset
                    {
                        Name = "Existing home", Kind = AssetKind.RealEstate, CurrentValue = 260_000m,
                        Currency = "EUR", ValuedOn = new DateOnly(2026, 7, 1), ValuationSource = "Personal estimate"
                    }
                ]
            };
            database.Scenarios.Add(scenario);
            database.ApplicationSettings.Add(new ApplicationSetting { Key = DataSchemaMigrationService.DataSchemaVersionKey, Value = "1" });
            await database.SaveChangesAsync();
            var service = new DataSchemaMigrationService(
                database,
                [new AssetValuationDataMigration(), new IncomeScheduleDataMigration(), new BankingDataMigration(), new EventCurrencyDataMigration(), new PlannedAssetSaleDataMigration(), new OwnershipDataMigration()],
                NullLogger<DataSchemaMigrationService>.Instance);

            await service.EnsureCurrentAsync();

            var snapshot = await database.AssetValuationSnapshots.SingleAsync();
            Assert.Equal(260_000m, snapshot.Value);
            Assert.Equal(new DateOnly(2026, 7, 1), snapshot.ValuedOn);
            Assert.Equal("Personal estimate", snapshot.Source);
            var setting = await database.ApplicationSettings.FindAsync(DataSchemaMigrationService.DataSchemaVersionKey);
            Assert.Equal("7", setting!.Value);
        }
        finally
        {
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}-shm");
            DeleteIfExists($"{databasePath}-wal");
        }
    }

    /// <summary>Backfills an annual reference for income saved before flexible income schedules existed.</summary>
    [Fact]
    public async Task Version_two_monthly_income_receives_an_annual_reference_amount()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-income-version-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LifeLedgerDbContext>().UseSqlite($"Data Source={databasePath}").Options;

        try
        {
            await using var database = new LifeLedgerDbContext(options);
            await database.Database.EnsureCreatedAsync();
            database.Scenarios.Add(new FinancialScenario
            {
                Profile = new Profile { DisplayName = "Income migration profile" },
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Incomes = [new IncomeStream { Name = "Existing rent", Kind = IncomeKind.Rental, MonthlyAmount = 1_500m, Currency = "EUR" }]
            });
            database.ApplicationSettings.Add(new ApplicationSetting { Key = DataSchemaMigrationService.DataSchemaVersionKey, Value = "2" });
            await database.SaveChangesAsync();

            var service = new DataSchemaMigrationService(database, [new IncomeScheduleDataMigration(), new BankingDataMigration(), new EventCurrencyDataMigration(), new PlannedAssetSaleDataMigration(), new OwnershipDataMigration()], NullLogger<DataSchemaMigrationService>.Instance);
            await service.EnsureCurrentAsync();

            var income = await database.Incomes.SingleAsync();
            Assert.Equal(18_000m, income.AnnualAmount);
            Assert.Equal(IncomeAmountMode.Monthly, income.AmountMode);
            var setting = await database.ApplicationSettings.FindAsync(DataSchemaMigrationService.DataSchemaVersionKey);
            Assert.Equal("7", setting!.Value);
        }
        finally
        {
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}-shm");
            DeleteIfExists($"{databasePath}-wal");
        }
    }

    /// <summary>Preserves the historical meaning of event amounts by assigning the owning profile's base currency.</summary>
    [Fact]
    public async Task Version_four_events_receive_the_profile_currency()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-event-currency-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<LifeLedgerDbContext>().UseSqlite($"Data Source={databasePath}").Options;

        try
        {
            await using var database = new LifeLedgerDbContext(options);
            await database.Database.EnsureCreatedAsync();
            database.Scenarios.Add(new FinancialScenario
            {
                Profile = new Profile { DisplayName = "Event currency profile", BaseCurrency = "PLN" },
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Events = [new ScenarioEvent { Name = "Existing purchase", Kind = EventKind.HousePurchase, HappensOn = new DateOnly(2030, 1, 1), OneOffCashImpact = -100_000m, Currency = "EUR" }]
            });
            database.ApplicationSettings.Add(new ApplicationSetting { Key = DataSchemaMigrationService.DataSchemaVersionKey, Value = "4" });
            await database.SaveChangesAsync();

            var service = new DataSchemaMigrationService(database, [new EventCurrencyDataMigration(), new PlannedAssetSaleDataMigration(), new OwnershipDataMigration()], NullLogger<DataSchemaMigrationService>.Instance);
            await service.EnsureCurrentAsync();

            Assert.Equal("PLN", (await database.Events.SingleAsync()).Currency);
            Assert.Equal("7", (await database.ApplicationSettings.FindAsync(DataSchemaMigrationService.DataSchemaVersionKey))!.Value);
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
