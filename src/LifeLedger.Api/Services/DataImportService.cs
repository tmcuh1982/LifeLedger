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
            await db.AssetQuoteSnapshots.ExecuteDeleteAsync(cancellationToken);
            await db.NetWorthSnapshots.ExecuteDeleteAsync(cancellationToken);
            await db.Scenarios.ExecuteDeleteAsync(cancellationToken);
            await db.Profiles.ExecuteDeleteAsync(cancellationToken);
        }

        var profile = request.Document.Profile;
        PrepareProfile(profile);
        db.Profiles.Add(profile);

        foreach (var scenario in request.Document.Scenarios)
        {
            PrepareScenario(scenario, profile.Id);
            db.Scenarios.Add(scenario);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return profile.Id;
    }

    /// <summary>Validates the portable document before any existing local record can be removed.</summary>
    private static void Validate(LifeLedgerExport document)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (document.SchemaVersion != 1) errors["document.schemaVersion"] = ["Unsupported export schema."];
        if (string.IsNullOrWhiteSpace(document.Profile.DisplayName)) errors["document.profile.displayName"] = ["A profile name is required."];
        if (!IsCurrencyCode(document.Profile.BaseCurrency)) errors["document.profile.baseCurrency"] = ["Use a three-letter ISO 4217 currency code."];
        if (document.Profile.ExpectedLifespan is < 50 or > 130) errors["document.profile.expectedLifespan"] = ["The final age must be between 50 and 130."];

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
                scenario.Expenses.Any(expense => !IsCurrencyCode(expense.Currency)))
            {
                errors[$"{prefix}.currency"] = ["Every financial entry must use a three-letter ISO 4217 currency code."];
            }
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
    private static void PrepareScenario(FinancialScenario scenario, Guid profileId)
    {
        scenario.Id = Guid.NewGuid();
        scenario.ProfileId = profileId;
        scenario.Profile = null;
        scenario.ParentScenarioId = null;
        scenario.Name = scenario.Name.Trim();
        scenario.Assumptions.Id = Guid.NewGuid();
        scenario.Assumptions.ScenarioId = scenario.Id;
        scenario.Assumptions.Scenario = null;

        foreach (var item in scenario.Incomes) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; item.Currency = item.Currency.Trim().ToUpperInvariant(); }
        foreach (var item in scenario.Assets) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; item.Currency = item.Currency.Trim().ToUpperInvariant(); }
        foreach (var item in scenario.Liabilities) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; item.Currency = item.Currency.Trim().ToUpperInvariant(); }
        foreach (var item in scenario.Expenses) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; item.Currency = item.Currency.Trim().ToUpperInvariant(); }
        foreach (var item in scenario.Investments) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
        foreach (var item in scenario.Events) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    }

    /// <summary>Checks the minimal shape of an ISO 4217 currency code without relying on a remote catalogue.</summary>
    private static bool IsCurrencyCode(string? value) => value is { Length: 3 } && value.All(char.IsAsciiLetter);
}
