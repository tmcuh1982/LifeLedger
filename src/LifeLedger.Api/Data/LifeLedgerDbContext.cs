using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Data;

public sealed class LifeLedgerDbContext(DbContextOptions<LifeLedgerDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<CareerPeriod> CareerPeriods => Set<CareerPeriod>();
    public DbSet<FinancialScenario> Scenarios => Set<FinancialScenario>();
    public DbSet<SimulationAssumptions> Assumptions => Set<SimulationAssumptions>();
    public DbSet<IncomeStream> Incomes => Set<IncomeStream>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetQuoteSnapshot> AssetQuoteSnapshots => Set<AssetQuoteSnapshot>();
    public DbSet<Liability> Liabilities => Set<Liability>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<InvestmentPlan> Investments => Set<InvestmentPlan>();
    public DbSet<ScenarioEvent> Events => Set<ScenarioEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(property => property.ClrType == typeof(decimal)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }

        modelBuilder.Entity<Profile>().HasIndex(x => x.DisplayName);
        modelBuilder.Entity<FinancialScenario>().HasIndex(x => new { x.ProfileId, x.IsBaseline });
        modelBuilder.Entity<FinancialScenario>().HasOne(x => x.Assumptions)
            .WithOne(x => x.Scenario)
            .HasForeignKey<SimulationAssumptions>(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Incomes).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Assets).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AssetQuoteSnapshot>().HasIndex(x => new { x.AssetId, x.CapturedAt });
        modelBuilder.Entity<AssetQuoteSnapshot>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Liabilities).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Expenses).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Investments).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FinancialScenario>().HasMany(x => x.Events).WithOne(x => x.Scenario).HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
