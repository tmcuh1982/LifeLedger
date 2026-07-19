using System.Text.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Imports a portable LifeLedger export without leaving the local database partially changed.</summary>
public interface IDataImportService
{
    /// <summary>Validates and imports a document, optionally replacing every existing financial record.</summary>
    Task<Guid> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Contains field-level validation errors that can safely be shown by the REST API.</summary>
public sealed class ImportValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The import document is invalid.")
{
    /// <summary>Validation messages indexed by the affected document field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>EF Core implementation of the local data-import transaction.</summary>
public sealed class DataImportService(LifeLedgerDbContext db) : IDataImportService
{
    /// <inheritdoc />
    public async Task<Guid> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Document);

        // Validation happens before opening the destructive replacement transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.ReplaceExisting)
        {
            // Delete dependants explicitly so the operation also works with strict foreign-key configurations.
            await db.BankTransactions.ExecuteDeleteAsync(cancellationToken);
            await db.BankStatementImports.ExecuteDeleteAsync(cancellationToken);
            await db.BankAccounts.ExecuteDeleteAsync(cancellationToken);
            await db.AssetQuoteSnapshots.ExecuteDeleteAsync(cancellationToken);
            await db.AssetValuationSnapshots.ExecuteDeleteAsync(cancellationToken);
            await db.NetWorthSnapshots.ExecuteDeleteAsync(cancellationToken);
            await db.Scenarios.ExecuteDeleteAsync(cancellationToken);
            await db.Profiles.ExecuteDeleteAsync(cancellationToken);
        }

        var profile = request.Document.Profile;
        PrepareProfile(profile);
        db.Profiles.Add(profile);

        foreach (var scenario in request.Document.Scenarios)
        {
            PrepareScenario(scenario, profile.Id, profile.BaseCurrency, request.Document.SchemaVersion);
            db.Scenarios.Add(scenario);
        }

        await MergeAssetProfileDefinitionsAsync(request.Document.AssetProfileDefinitions ?? [], request.ReplaceExisting, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return profile.Id;
    }

    /// <summary>Validates the portable document before any existing local record can be removed.</summary>
    private static void Validate(LifeLedgerExport document)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        // Older versions remain importable; version 11 adds explicit planned asset sales.
        if (document.SchemaVersion is < 1 or > 11) errors["document.schemaVersion"] = ["Unsupported export schema."];
        if (string.IsNullOrWhiteSpace(document.Profile.DisplayName)) errors["document.profile.displayName"] = ["A profile name is required."];
        if (!IsCurrencyCode(document.Profile.BaseCurrency)) errors["document.profile.baseCurrency"] = ["Use a three-letter ISO 4217 currency code."];
        if (document.Profile.ExpectedLifespan is < 50 or > 130) errors["document.profile.expectedLifespan"] = ["The final age must be between 50 and 130."];
        if ((document.AssetProfileDefinitions ?? []).Any(definition => !definition.IsCustom || !definition.Key.StartsWith("custom-", StringComparison.Ordinal) || definition.Version < 1 || definition.Fields.Count == 0))
            errors["document.assetProfileDefinitions"] = ["One or more custom asset-sheet definitions are invalid."];

        for (var index = 0; index < document.Scenarios.Count; index++)
        {
            var scenario = document.Scenarios[index];
            var prefix = $"document.scenarios[{index}]";
            if (string.IsNullOrWhiteSpace(scenario.Name)) errors[$"{prefix}.name"] = ["A scenario name is required."];
            if (scenario.Assumptions.RetirementAge is < 40 or > 100) errors[$"{prefix}.assumptions.retirementAge"] = ["Retirement age must be between 40 and 100."];
            if (scenario.Assumptions.MonteCarloRuns is < 50 or > 5_000) errors[$"{prefix}.assumptions.monteCarloRuns"] = ["Monte Carlo runs must be between 50 and 5000."];
            if (scenario.Incomes.Any(income => !IsCurrencyCode(income.Currency)) ||
                scenario.Assets.Any(asset => !IsCurrencyCode(asset.Currency)) ||
                scenario.Liabilities.Any(liability => !IsCurrencyCode(liability.Currency)) ||
                scenario.Expenses.Any(expense => !IsCurrencyCode(expense.Currency)) ||
                scenario.AssetSales.Any(sale => !IsCurrencyCode(sale.Currency)) ||
                scenario.Events.Any(scenarioEvent => !IsCurrencyCode(scenarioEvent.Currency)) ||
                scenario.BankAccounts.Any(account => !IsCurrencyCode(account.Currency)) ||
                scenario.BankAccounts.SelectMany(account => account.Imports).SelectMany(statement => statement.Transactions).Any(transaction => !IsCurrencyCode(transaction.Currency)))
            {
                errors[$"{prefix}.currency"] = ["Every financial entry must use a three-letter ISO 4217 currency code."];
            }
            if (scenario.Expenses.Any(expense => expense.AmountChanges.Any(change =>
                    expense.Kind != ExpenseKind.Recurring || change.Amount < 0 || change.EffectiveOn < expense.StartsOn ||
                    (expense.EndsOn is { } endsOn && change.EffectiveOn > endsOn)) ||
                expense.AmountChanges.GroupBy(change => change.EffectiveOn).Any(group => group.Count() > 1)))
            {
                errors[$"{prefix}.expenses.amountChanges"] = ["One or more recurring-expense amount changes are invalid."];
            }
            if (scenario.AllocationStrategies.Any(strategy => string.IsNullOrWhiteSpace(strategy.Name) || strategy.EffectiveTo is { } end && end < strategy.EffectiveFrom || strategy.Targets.Count == 0 || strategy.Targets.Sum(target => target.TargetPercentage) > 100m || strategy.Targets.Any(target => string.IsNullOrWhiteSpace(target.Category) || target.TargetPercentage is < 0m or > 100m || target.TolerancePercentage is < 0m or > 100m)))
                errors[$"{prefix}.allocationStrategies"] = ["One or more allocation strategies are invalid."];
            if (scenario.AssetSales.Any(sale => string.IsNullOrWhiteSpace(sale.Name) || sale.HappensOn < scenario.StartsOn || sale.SellingCosts < 0m || sale.CapitalGainsTaxRate is < 0m or > 1m ||
                    !scenario.Assets.Any(asset => asset.Id == sale.AssetId) ||
                    sale.Destination == AssetSaleDestination.Asset && (sale.DestinationAssetId is null || sale.DestinationAssetId == sale.AssetId || !scenario.Assets.Any(asset => asset.Id == sale.DestinationAssetId)) ||
                    sale.Destination == AssetSaleDestination.InvestmentPlan && (sale.DestinationInvestmentPlanId is null || !scenario.Investments.Any(plan => plan.Id == sale.DestinationInvestmentPlanId))) ||
                scenario.AssetSales.GroupBy(sale => sale.AssetId).Any(group => group.Count() > 1))
                errors[$"{prefix}.assetSales"] = ["One or more planned asset sales are invalid."];
        }

        if (errors.Count > 0) throw new ImportValidationException(errors);
    }

    /// <summary>Assigns a new local identity to the imported profile and all career periods.</summary>
    private static void PrepareProfile(Profile profile)
    {
        profile.Id = Guid.NewGuid();
        profile.DisplayName = profile.DisplayName.Trim();
        profile.BaseCurrency = profile.BaseCurrency.Trim().ToUpperInvariant();
        profile.HomeCountryCode = profile.HomeCountryCode.Trim().ToUpperInvariant();
        foreach (var career in profile.Careers)
        {
            career.Id = Guid.NewGuid();
            career.ProfileId = profile.Id;
            career.Profile = null;
            career.CountryCode = career.CountryCode.Trim().ToUpperInvariant();
        }
    }

    /// <summary>Assigns new identities and ownership to a scenario and its dependent entries.</summary>
    private static void PrepareScenario(FinancialScenario scenario, Guid profileId, string profileCurrency, int exportSchemaVersion)
    {
        scenario.Id = Guid.NewGuid();
        scenario.ProfileId = profileId;
        scenario.Profile = null;
        scenario.ParentScenarioId = null;
        scenario.Name = scenario.Name.Trim();
        scenario.Assumptions.Id = Guid.NewGuid();
        scenario.Assumptions.ScenarioId = scenario.Id;
        scenario.Assumptions.Scenario = null;

        foreach (var item in scenario.Incomes)
        {
            item.Id = Guid.NewGuid();
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
            item.LinkedAsset = null;
            item.Currency = item.Currency.Trim().ToUpperInvariant();
            foreach (var allocation in item.MonthlyAllocations)
            {
                allocation.IncomeStreamId = item.Id;
                allocation.IncomeStream = null;
            }
        }
        // Liabilities are re-keyed first so asset links can be remapped without retaining foreign IDs from a backup.
        var liabilityIds = new Dictionary<Guid, Guid>();
        foreach (var item in scenario.Liabilities)
        {
            var oldId = item.Id;
            item.Id = Guid.NewGuid();
            liabilityIds[oldId] = item.Id;
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
            item.AssetLinks = [];
            item.Currency = item.Currency.Trim().ToUpperInvariant();
        }
        var assetIds = new Dictionary<Guid, Guid>();
        foreach (var item in scenario.Assets)
        {
            var oldId = item.Id;
            item.Id = Guid.NewGuid();
            assetIds[oldId] = item.Id;
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
            item.Currency = item.Currency.Trim().ToUpperInvariant();
            if (item.CharacteristicProfile is not null)
            {
                item.CharacteristicProfile.AssetId = item.Id;
                item.CharacteristicProfile.Asset = null;
            }
            foreach (var snapshot in item.ValuationSnapshots)
            {
                snapshot.Id = Guid.NewGuid();
                snapshot.AssetId = item.Id;
                snapshot.Asset = null;
                snapshot.Currency = snapshot.Currency.Trim().ToUpperInvariant();
            }
            if (item.ValuationSnapshots.Count == 0)
            {
                // Export schemas 1 and 2 predate valuation history, so their current value becomes the first point.
                item.ValuationSnapshots.Add(new AssetValuationSnapshot
                {
                    AssetId = item.Id,
                    ValuedOn = item.ValuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    Value = item.CurrentValue,
                    Currency = item.Currency,
                    Source = string.IsNullOrWhiteSpace(item.ValuationSource) ? "Imported current value" : item.ValuationSource
                });
            }
            item.LiabilityLinks = item.LiabilityLinks
                .Where(link => liabilityIds.ContainsKey(link.LiabilityId))
                .Select(link => new AssetLiabilityLink { AssetId = item.Id, LiabilityId = liabilityIds[link.LiabilityId], AllocationRate = link.AllocationRate })
                .ToList();
        }
        foreach (var item in scenario.Incomes)
            item.LinkedAssetId = item.LinkedAssetId is { } oldAssetId && assetIds.TryGetValue(oldAssetId, out var newAssetId) ? newAssetId : null;
        foreach (var item in scenario.Expenses)
        {
            item.Id = Guid.NewGuid();
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
            item.LinkedAsset = null;
            item.LinkedAssetId = item.LinkedAssetId is { } oldAssetId && assetIds.TryGetValue(oldAssetId, out var newAssetId) ? newAssetId : null;
            item.Currency = item.Currency.Trim().ToUpperInvariant();
            item.ObservedBankCategory = string.IsNullOrWhiteSpace(item.ObservedBankCategory) ? null : item.ObservedBankCategory.Trim().ToLowerInvariant();
            item.AmountChanges = item.Kind == ExpenseKind.Recurring
                ? item.AmountChanges.OrderBy(change => change.EffectiveOn).ToList()
                : [];
            foreach (var change in item.AmountChanges)
            {
                change.Id = Guid.NewGuid();
                change.ExpenseId = item.Id;
                change.Expense = null;
            }
        }
        var investmentIds = new Dictionary<Guid, Guid>();
        foreach (var item in scenario.Investments)
        {
            var oldId = item.Id;
            item.Id = Guid.NewGuid();
            investmentIds[oldId] = item.Id;
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
        }
        foreach (var sale in scenario.AssetSales)
        {
            sale.Id = Guid.NewGuid();
            sale.ScenarioId = scenario.Id;
            sale.Scenario = null;
            sale.Asset = null;
            sale.AssetId = assetIds.GetValueOrDefault(sale.AssetId);
            sale.DestinationAsset = null;
            sale.DestinationAssetId = sale.DestinationAssetId is { } oldDestinationAssetId && assetIds.TryGetValue(oldDestinationAssetId, out var newDestinationAssetId) ? newDestinationAssetId : null;
            sale.DestinationInvestmentPlan = null;
            sale.DestinationInvestmentPlanId = sale.DestinationInvestmentPlanId is { } oldDestinationPlanId && investmentIds.TryGetValue(oldDestinationPlanId, out var newDestinationPlanId) ? newDestinationPlanId : null;
            sale.Currency = sale.Currency.Trim().ToUpperInvariant();
        }
        foreach (var item in scenario.Events)
        {
            item.Id = Guid.NewGuid();
            item.ScenarioId = scenario.Id;
            item.Scenario = null;
            // The old projection engine interpreted event amounts directly in the owning profile's base currency.
            item.Currency = exportSchemaVersion < 6 ? profileCurrency : item.Currency.Trim().ToUpperInvariant();
        }
        foreach (var account in scenario.BankAccounts)
        {
            account.Id = Guid.NewGuid();
            account.ScenarioId = scenario.Id;
            account.Scenario = null;
            account.LinkedAsset = null;
            account.LinkedAssetId = account.LinkedAssetId is { } oldAssetId && assetIds.TryGetValue(oldAssetId, out var newAssetId) ? newAssetId : null;
            account.Currency = account.Currency.Trim().ToUpperInvariant();
            foreach (var statementImport in account.Imports)
            {
                statementImport.Id = Guid.NewGuid();
                statementImport.BankAccountId = account.Id;
                statementImport.BankAccount = null;
                foreach (var bankTransaction in statementImport.Transactions)
                {
                    bankTransaction.Id = Guid.NewGuid();
                    bankTransaction.BankStatementImportId = statementImport.Id;
                    bankTransaction.BankStatementImport = null;
                    bankTransaction.LinkedAsset = null;
                    bankTransaction.LinkedAssetId = bankTransaction.LinkedAssetId is { } linkedAssetId && assetIds.TryGetValue(linkedAssetId, out var mappedAssetId) ? mappedAssetId : null;
                    bankTransaction.LinkedInvestmentPlan = null;
                    bankTransaction.LinkedInvestmentPlanId = bankTransaction.LinkedInvestmentPlanId is { } linkedPlanId && investmentIds.TryGetValue(linkedPlanId, out var mappedPlanId) ? mappedPlanId : null;
                    bankTransaction.Currency = bankTransaction.Currency.Trim().ToUpperInvariant();
                }
            }
        }
        foreach (var strategy in scenario.AllocationStrategies)
        {
            strategy.Id = Guid.NewGuid();
            strategy.ScenarioId = scenario.Id;
            strategy.Scenario = null;
            strategy.Name = strategy.Name.Trim();
            strategy.Description = string.IsNullOrWhiteSpace(strategy.Description) ? null : strategy.Description.Trim();
            foreach (var target in strategy.Targets)
            {
                target.Id = Guid.NewGuid();
                target.AllocationStrategyId = strategy.Id;
                target.AllocationStrategy = null;
                target.Category = target.Category.Trim();
            }
        }
    }

    /// <summary>Merges backed-up custom schema versions without discarding unrelated local definitions.</summary>
    private async Task MergeAssetProfileDefinitionsAsync(IReadOnlyList<AssetProfileDefinition> imported, bool replaceExisting, CancellationToken cancellationToken)
    {
        var setting = await db.ApplicationSettings.FindAsync([AssetProfileCatalog.SettingKey], cancellationToken);
        var existing = new List<AssetProfileDefinition>();
        if (!replaceExisting && setting is not null)
        {
            try { existing = JsonSerializer.Deserialize<List<AssetProfileDefinition>>(setting.Value) ?? []; }
            catch (JsonException) { throw new ImportValidationException(new Dictionary<string, string[]> { ["document.assetProfileDefinitions"] = ["The local asset-sheet catalogue is malformed."] }); }
        }
        var merged = existing.Concat(imported)
            .GroupBy(definition => new { definition.Key, definition.Version })
            .Select(group => group.Last())
            .OrderBy(definition => definition.Key)
            .ThenBy(definition => definition.Version)
            .ToArray();
        if (setting is null) db.ApplicationSettings.Add(new ApplicationSetting { Key = AssetProfileCatalog.SettingKey, Value = JsonSerializer.Serialize(merged) });
        else { setting.Value = JsonSerializer.Serialize(merged); setting.UpdatedAt = DateTimeOffset.UtcNow; }
    }

    /// <summary>Checks the minimal shape of an ISO 4217 currency code without relying on a remote catalogue.</summary>
    private static bool IsCurrencyCode(string? value) => value is { Length: 3 } && value.All(char.IsAsciiLetter);
}
