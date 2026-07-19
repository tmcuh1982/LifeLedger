using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Native base class for read-only broker portfolio connections that synchronize external holdings into a scenario.</summary>
public abstract class FlexService(
    LifeLedgerDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    IAssetValuationHistoryService valuationHistory)
{
    /// <summary>EF Core unit of work for local settings and imported assets.</summary>
    protected LifeLedgerDbContext Database { get; } = db;
    /// <summary>Local valuation-history interface shared by every portfolio connector.</summary>
    protected IAssetValuationHistoryService ValuationHistory { get; } = valuationHistory;
    /// <summary>Protects connection credentials at rest using installation-local key material.</summary>
    protected IDataProtector CredentialProtector { get; } = dataProtectionProvider.CreateProtector("LifeLedger.FlexService.Credentials.v1");

    /// <summary>Stable key used to separate this connector's local configuration from other providers.</summary>
    protected abstract string ConnectionKey { get; }
    /// <summary>Human-readable provider name recorded on assets created by this connector.</summary>
    protected abstract string ProviderName { get; }

    /// <summary>Loads the scenario and its imported assets for an idempotent synchronization.</summary>
    protected async Task<FinancialScenario> LoadScenarioAsync(Guid scenarioId, CancellationToken cancellationToken) =>
        await Database.Scenarios.Include(candidate => candidate.Assets).SingleOrDefaultAsync(candidate => candidate.Id == scenarioId, cancellationToken)
        ?? throw new KeyNotFoundException("The scenario does not exist.");

    /// <summary>Reads a protected or ordinary local setting owned by this connector.</summary>
    protected Task<string?> ReadConnectionSettingAsync(Guid scenarioId, string name, CancellationToken cancellationToken) =>
        Database.ApplicationSettings.Where(setting => setting.Key == SettingKey(scenarioId, name)).Select(setting => setting.Value).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Creates or updates a local connector setting without exposing it to exports or API reads.</summary>
    protected async Task WriteConnectionSettingAsync(Guid scenarioId, string name, string value, CancellationToken cancellationToken)
    {
        var key = SettingKey(scenarioId, name);
        var setting = await Database.ApplicationSettings.SingleOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);
        if (setting is null) Database.ApplicationSettings.Add(new ApplicationSetting { Key = key, Value = value });
        else { setting.Value = value; setting.UpdatedAt = DateTimeOffset.UtcNow; }
    }

    /// <summary>Returns whether this connector owns an imported asset with the supplied provider identifier.</summary>
    protected bool IsImportedAsset(Asset asset, string externalId) => asset.ExternalProvider == ProviderName && asset.ExternalId == externalId;

    /// <summary>Builds one namespaced key for configuration local to a scenario and connection type.</summary>
    private string SettingKey(Guid scenarioId, string name) => $"flex:{ConnectionKey}:{scenarioId:N}:{name}";
}
