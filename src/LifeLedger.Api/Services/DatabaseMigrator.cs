using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Applies schema migrations before the application reads or writes financial data.</summary>
public interface IDatabaseMigrator
{
    /// <summary>Brings the configured database schema up to the latest known version.</summary>
    Task ApplyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies EF Core migrations and safely adopts SQLite databases created before migrations existed.
/// </summary>
public sealed class DatabaseMigrator(
    LifeLedgerDbContext db,
    ILogger<DatabaseMigrator> logger) : IDatabaseMigrator
{
    /// <summary>EF Core table that records migrations already applied to a database.</summary>
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    /// <inheritdoc />
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlite())
        {
            await BaselineLegacySqliteDatabaseAsync(cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations are up to date.");
    }

    /// <summary>Marks legacy SQLite databases as baselined before applying later migrations.</summary>
    private async Task BaselineLegacySqliteDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync("Profiles", cancellationToken) ||
            await TableExistsAsync(MigrationHistoryTable, cancellationToken))
        {
            return;
        }

        // Mark only the original schema as applied; later EF migrations still need to create their tables.
        var initialMigration = db.Database.GetMigrations().FirstOrDefault();
        if (initialMigration is null)
        {
            return;
        }

        logger.LogInformation(
            "Adopting an existing SQLite database created before EF Core migrations were enabled.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)",
            cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({initialMigration}, {"9.0.0"})",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Checks SQLite metadata without assuming an EF migration table already exists.</summary>
    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }
}
