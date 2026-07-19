using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Transforms persisted business data between explicitly versioned application formats.</summary>
public interface IDataSchemaMigration
{
    /// <summary>Version expected before this transformation runs.</summary>
    int FromVersion { get; }
    /// <summary>Version written after this transformation succeeds.</summary>
    int ToVersion { get; }
    /// <summary>Applies the data transformation inside the migration transaction.</summary>
    Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken);
}

/// <summary>Ensures the local business-data format is upgraded safely after EF schema migrations.</summary>
public interface IDataSchemaMigrationService
{
    /// <summary>Latest data format supported by this application version.</summary>
    int CurrentVersion { get; }
    /// <summary>Applies every required contiguous data transformation to the local database.</summary>
    Task EnsureCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>Stores the current business-data version in <c>ApplicationSettings</c> and upgrades it sequentially.</summary>
public sealed class DataSchemaMigrationService(
    LifeLedgerDbContext db,
    IEnumerable<IDataSchemaMigration> migrations,
    ILogger<DataSchemaMigrationService> logger) : IDataSchemaMigrationService
{
    /// <summary>Key used in the local settings table for the business-data format version.</summary>
    public const string DataSchemaVersionKey = "data-schema-version";
    /// <summary>Latest business-data format understood by this application version.</summary>
    public const int LatestVersion = 6;

    /// <inheritdoc />
    public int CurrentVersion => LatestVersion;

    /// <inheritdoc />
    public async Task EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        var versionSetting = await db.ApplicationSettings.FindAsync([DataSchemaVersionKey], cancellationToken);
        if (versionSetting is null)
        {
            // Existing installations were already in the first supported business-data format.
            db.ApplicationSettings.Add(new ApplicationSetting
            {
                Key = DataSchemaVersionKey,
                Value = LatestVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Initialised local data schema version {Version}", LatestVersion);
            return;
        }

        if (!int.TryParse(versionSetting.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var installedVersion) || installedVersion < 1)
        {
            throw new InvalidOperationException("The stored data schema version is invalid.");
        }
        if (installedVersion > LatestVersion)
        {
            throw new InvalidOperationException($"This database uses data schema version {installedVersion}, which is newer than this LifeLedger installation supports.");
        }

        while (installedVersion < LatestVersion)
        {
            var migration = migrations.SingleOrDefault(candidate => candidate.FromVersion == installedVersion && candidate.ToVersion == installedVersion + 1)
                ?? throw new InvalidOperationException($"No data migration is available from version {installedVersion} to {installedVersion + 1}.");

            // Each step is independent and atomic: a failed transformation leaves the prior version untouched.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await migration.ApplyAsync(db, cancellationToken);
            installedVersion = migration.ToVersion;
            versionSetting.Value = installedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            versionSetting.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Migrated local data schema to version {Version}", installedVersion);
        }
    }
}
