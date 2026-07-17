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
    /// <summary>Assets belonging to scenarios.</summary>
    public DbSet<Asset> Assets => Set<Asset>();
    /// <summary>Locally stored market-price observations.</summary>
    public DbSet<AssetQuoteSnapshot> AssetQuoteSnapshots => Set<AssetQuoteSnapshot>();
    /// <summary>Liabilities belonging to scenarios.</summary>
    public DbSet<Liability> Liabilities => Set<Liability>();
    /// <summary>Expenses belonging to scenarios.</summary>
    public DbSet<Expense> Expenses => Set<Expense>();
    /// <summary>Regular investment plans belonging to scenarios.</summary>
    public DbSet<InvestmentPlan> Investments => Set<InvestmentPlan>();
    /// <summary>Life events belonging to scenarios.</summary>
    public DbSet<ScenarioEvent> Events => Set<ScenarioEvent>();

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
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Assets).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Asset>().Property(x => x.CustomCategory).HasMaxLength(80);
        modelBuilder.Entity<AssetQuoteSnapshot>().HasIndex(x => new { x.AssetId, x.CapturedAt });
        modelBuilder.Entity<AssetQuoteSnapshot>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Liabilities).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Expenses).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Investments).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Events).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
