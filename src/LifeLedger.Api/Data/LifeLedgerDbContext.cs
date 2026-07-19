using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Data;

/// <summary>EF Core persistence boundary for the profile, scenarios, and financial entries.</summary>
public sealed class LifeLedgerDbContext(DbContextOptions<LifeLedgerDbContext> options) : DbContext(options)
{
    /// <summary>Local application metadata, including the business-data schema version.</summary>
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    /// <summary>Profiles stored in the local database.</summary>
    public DbSet<Profile> Profiles => Set<Profile>();
    /// <summary>Cross-country career periods.</summary>
    public DbSet<CareerPeriod> CareerPeriods => Set<CareerPeriod>();
    /// <summary>Locally captured net-worth history for profiles.</summary>
    public DbSet<NetWorthSnapshot> NetWorthSnapshots => Set<NetWorthSnapshot>();
    /// <summary>Financial scenarios owned by profiles.</summary>
    public DbSet<FinancialScenario> Scenarios => Set<FinancialScenario>();
    /// <summary>Per-scenario projection assumptions.</summary>
    public DbSet<SimulationAssumptions> Assumptions => Set<SimulationAssumptions>();
    /// <summary>Income streams belonging to scenarios.</summary>
    public DbSet<IncomeStream> Incomes => Set<IncomeStream>();
    /// <summary>Calendar-month allocations used by seasonal income streams.</summary>
    public DbSet<IncomeMonthlyAllocation> IncomeMonthlyAllocations => Set<IncomeMonthlyAllocation>();
    /// <summary>Assets belonging to scenarios.</summary>
    public DbSet<Asset> Assets => Set<Asset>();
    /// <summary>Locally stored market-price observations.</summary>
    public DbSet<AssetQuoteSnapshot> AssetQuoteSnapshots => Set<AssetQuoteSnapshot>();
    /// <summary>Locally stored total-value observations for every kind of asset.</summary>
    public DbSet<AssetValuationSnapshot> AssetValuationSnapshots => Set<AssetValuationSnapshot>();
    /// <summary>Versioned characteristic sheets attached one-to-one to assets.</summary>
    public DbSet<AssetCharacteristicProfile> AssetCharacteristicProfiles => Set<AssetCharacteristicProfile>();
    /// <summary>Allocation links between assets and the liabilities that finance them.</summary>
    public DbSet<AssetLiabilityLink> AssetLiabilityLinks => Set<AssetLiabilityLink>();
    /// <summary>Liabilities belonging to scenarios.</summary>
    public DbSet<Liability> Liabilities => Set<Liability>();
    /// <summary>Expenses belonging to scenarios.</summary>
    public DbSet<Expense> Expenses => Set<Expense>();
    /// <summary>Dated amount changes belonging to recurring expenses.</summary>
    public DbSet<ExpenseAmountChange> ExpenseAmountChanges => Set<ExpenseAmountChange>();
    /// <summary>Regular investment plans belonging to scenarios.</summary>
    public DbSet<InvestmentPlan> Investments => Set<InvestmentPlan>();
    /// <summary>Explicit future sales of scenario assets.</summary>
    public DbSet<PlannedAssetSale> AssetSales => Set<PlannedAssetSale>();
    /// <summary>Life events belonging to scenarios.</summary>
    public DbSet<ScenarioEvent> Events => Set<ScenarioEvent>();
    /// <summary>Locally registered bank accounts.</summary>
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    /// <summary>Committed bank-statement imports.</summary>
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    /// <summary>Historical operations extracted from bank statements.</summary>
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    /// <summary>Dated target-allocation strategy versions owned by scenarios.</summary>
    public DbSet<AllocationStrategy> AllocationStrategies => Set<AllocationStrategy>();
    /// <summary>Category target bands belonging to allocation-strategy versions.</summary>
    public DbSet<AllocationStrategyTarget> AllocationStrategyTargets => Set<AllocationStrategyTarget>();

    /// <summary>Configures precision, indexes, and cascading relationships for the financial model.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Decimal values represent money and percentages, so every provider receives the same precision.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(property => property.ClrType == typeof(decimal)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }

        modelBuilder.Entity<ApplicationSetting>().HasKey(x => x.Key);
        modelBuilder.Entity<ApplicationSetting>().Property(x => x.Key).HasMaxLength(128);
        modelBuilder.Entity<Profile>().HasIndex(x => x.DisplayName);
        modelBuilder.Entity<NetWorthSnapshot>().HasIndex(x => new { x.ProfileId, x.CapturedAt });
        modelBuilder.Entity<NetWorthSnapshot>().HasOne(x => x.Profile).WithMany(x => x.NetWorthSnapshots).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasIndex(x => new { x.ProfileId, x.IsBaseline });
        modelBuilder.Entity<FinancialScenario>().HasOne(x => x.Assumptions)
            .WithOne(x => x.Scenario)
            .HasForeignKey<SimulationAssumptions>(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // A scenario is the aggregate root: deleting it removes only its dependent financial entries.
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Incomes).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<IncomeMonthlyAllocation>().HasKey(x => new { x.IncomeStreamId, x.Month });
        modelBuilder.Entity<IncomeMonthlyAllocation>().HasOne(x => x.IncomeStream).WithMany(x => x.MonthlyAllocations).HasForeignKey(x => x.IncomeStreamId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<IncomeStream>().HasOne(x => x.LinkedAsset).WithMany().HasForeignKey(x => x.LinkedAssetId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Assets).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Asset>().Property(x => x.CustomCategory).HasMaxLength(80);
        modelBuilder.Entity<Asset>().Property(x => x.ValuationSource).HasMaxLength(160);
        modelBuilder.Entity<Asset>().Property(x => x.ExternalProvider).HasMaxLength(80);
        modelBuilder.Entity<Asset>().Property(x => x.ExternalId).HasMaxLength(160);
        modelBuilder.Entity<Asset>().Property(x => x.IsIncludedInPortfolioAllocation).HasDefaultValue(true);
        modelBuilder.Entity<Asset>().HasIndex(x => new { x.ScenarioId, x.ExternalProvider, x.ExternalId }).IsUnique();
        modelBuilder.Entity<AssetCharacteristicProfile>().HasKey(x => x.AssetId);
        modelBuilder.Entity<AssetCharacteristicProfile>().Property(x => x.DefinitionKey).HasMaxLength(80);
        modelBuilder.Entity<AssetCharacteristicProfile>().HasOne(x => x.Asset).WithOne(x => x.CharacteristicProfile).HasForeignKey<AssetCharacteristicProfile>(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AssetLiabilityLink>().HasKey(x => new { x.AssetId, x.LiabilityId });
        modelBuilder.Entity<AssetLiabilityLink>().HasOne(x => x.Asset).WithMany(x => x.LiabilityLinks).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AssetLiabilityLink>().HasOne(x => x.Liability).WithMany(x => x.AssetLinks).HasForeignKey(x => x.LiabilityId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AssetQuoteSnapshot>().HasIndex(x => new { x.AssetId, x.CapturedAt });
        modelBuilder.Entity<AssetQuoteSnapshot>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AssetValuationSnapshot>().HasIndex(x => new { x.AssetId, x.ValuedOn }).IsUnique();
        modelBuilder.Entity<AssetValuationSnapshot>().Property(x => x.Currency).HasMaxLength(3);
        modelBuilder.Entity<AssetValuationSnapshot>().Property(x => x.Source).HasMaxLength(160);
        modelBuilder.Entity<AssetValuationSnapshot>().HasOne(x => x.Asset).WithMany(x => x.ValuationSnapshots).HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Liabilities).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Expenses).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Expense>().HasOne(x => x.LinkedAsset).WithMany().HasForeignKey(x => x.LinkedAssetId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Expense>().Property(x => x.ObservedBankCategory).HasMaxLength(80);
        modelBuilder.Entity<Expense>().HasIndex(x => new { x.ScenarioId, x.ObservedBankCategory, x.Currency });
        modelBuilder.Entity<Expense>().HasMany(x => x.AmountChanges).WithOne(x => x.Expense).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExpenseAmountChange>().HasIndex(x => new { x.ExpenseId, x.EffectiveOn }).IsUnique();
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Investments).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.AssetSales).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlannedAssetSale>().Property(x => x.Name).HasMaxLength(120);
        modelBuilder.Entity<PlannedAssetSale>().Property(x => x.Currency).HasMaxLength(3);
        modelBuilder.Entity<PlannedAssetSale>().Property(x => x.CapitalGainsTaxCountryCode).HasMaxLength(2);
        modelBuilder.Entity<PlannedAssetSale>().Property(x => x.Notes).HasMaxLength(1_000);
        modelBuilder.Entity<PlannedAssetSale>().HasIndex(x => x.AssetId).IsUnique();
        modelBuilder.Entity<PlannedAssetSale>().HasIndex(x => new { x.ScenarioId, x.HappensOn });
        modelBuilder.Entity<PlannedAssetSale>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlannedAssetSale>().HasOne(x => x.DestinationAsset).WithMany().HasForeignKey(x => x.DestinationAssetId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<PlannedAssetSale>().HasOne(x => x.DestinationInvestmentPlan).WithMany().HasForeignKey(x => x.DestinationInvestmentPlanId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Events).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ScenarioEvent>().Property(x => x.Currency).HasMaxLength(3);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.BankAccounts).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.AllocationStrategies).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AllocationStrategy>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<AllocationStrategy>().Property(x => x.Description).HasMaxLength(1_000);
        modelBuilder.Entity<AllocationStrategy>().HasIndex(x => new { x.ScenarioId, x.EffectiveFrom });
        modelBuilder.Entity<AllocationStrategy>().HasMany(x => x.Targets).WithOne(x => x.AllocationStrategy).HasForeignKey(x => x.AllocationStrategyId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AllocationStrategyTarget>().Property(x => x.Category).HasMaxLength(80);
        modelBuilder.Entity<AllocationStrategyTarget>().HasIndex(x => new { x.AllocationStrategyId, x.Category }).IsUnique();
        modelBuilder.Entity<BankAccount>().Property(x => x.BankKey).HasMaxLength(80);
        modelBuilder.Entity<BankAccount>().Property(x => x.Currency).HasMaxLength(3);
        modelBuilder.Entity<BankAccount>().HasIndex(x => new { x.ScenarioId, x.IdentifierHash }).IsUnique();
        modelBuilder.Entity<BankAccount>().HasOne(x => x.LinkedAsset).WithMany().HasForeignKey(x => x.LinkedAssetId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BankStatementImport>().HasIndex(x => new { x.BankAccountId, x.SourceFingerprint }).IsUnique();
        modelBuilder.Entity<BankStatementImport>().HasOne(x => x.BankAccount).WithMany(x => x.Imports).HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BankTransaction>().Property(x => x.Currency).HasMaxLength(3);
        modelBuilder.Entity<BankTransaction>().Property(x => x.Category).HasMaxLength(80);
        modelBuilder.Entity<BankTransaction>().HasIndex(x => new { x.BankStatementImportId, x.Fingerprint }).IsUnique();
        modelBuilder.Entity<BankTransaction>().HasOne(x => x.BankStatementImport).WithMany(x => x.Transactions).HasForeignKey(x => x.BankStatementImportId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BankTransaction>().HasOne(x => x.LinkedAsset).WithMany().HasForeignKey(x => x.LinkedAssetId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<BankTransaction>().HasOne(x => x.LinkedInvestmentPlan).WithMany().HasForeignKey(x => x.LinkedInvestmentPlanId).OnDelete(DeleteBehavior.SetNull);
    }
}
