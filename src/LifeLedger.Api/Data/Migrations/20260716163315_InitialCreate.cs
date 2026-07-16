using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    HomeCountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedLifespan = table.Column<int>(type: "INTEGER", nullable: false),
                    PartnerBirthYear = table.Column<int>(type: "INTEGER", nullable: true),
                    ChildrenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareerPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AnnualInsurableIncome = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EstimatedMonthlyPublicPension = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PensionAge = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerPeriods_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentScenarioId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsBaseline = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenarios_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExpectedAnnualReturn = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Volatility = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    IsLiquid = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ticker = table.Column<string>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CapitalGainsTaxRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CapitalGainsTaxCountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InflationRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SalaryGrowthRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SafeWithdrawalRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RetirementAge = table.Column<int>(type: "INTEGER", nullable: false),
                    MonteCarloRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultReturnVolatility = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assumptions_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    HappensOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RecurrenceFrequency = table.Column<int>(type: "INTEGER", nullable: true),
                    RecurrenceEndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OneOffCashImpact = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MonthlyCashImpact = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    DurationMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    IndexedToInflation = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Incomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AnnualGrowthRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsTaxable = table.Column<bool>(type: "INTEGER", nullable: false),
                    TaxRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TaxCountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incomes_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Investments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MonthlyContribution = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExpectedAnnualReturn = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Investments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Investments_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Liabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OutstandingBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    InterestRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MonthlyPayment = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PaidOffOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Liabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Liabilities_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetQuoteSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetQuoteSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetQuoteSnapshots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetQuoteSnapshots_AssetId_CapturedAt",
                table: "AssetQuoteSnapshots",
                columns: new[] { "AssetId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ScenarioId",
                table: "Assets",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Assumptions_ScenarioId",
                table: "Assumptions",
                column: "ScenarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerPeriods_ProfileId",
                table: "CareerPeriods",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ScenarioId",
                table: "Events",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ScenarioId",
                table: "Expenses",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Incomes_ScenarioId",
                table: "Incomes",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Investments_ScenarioId",
                table: "Investments",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Liabilities_ScenarioId",
                table: "Liabilities",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_DisplayName",
                table: "Profiles",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_ProfileId_IsBaseline",
                table: "Scenarios",
                columns: new[] { "ProfileId", "IsBaseline" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetQuoteSnapshots");

            migrationBuilder.DropTable(
                name: "Assumptions");

            migrationBuilder.DropTable(
                name: "CareerPeriods");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Incomes");

            migrationBuilder.DropTable(
                name: "Investments");

            migrationBuilder.DropTable(
                name: "Liabilities");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Scenarios");

            migrationBuilder.DropTable(
                name: "Profiles");
        }
    }
}
