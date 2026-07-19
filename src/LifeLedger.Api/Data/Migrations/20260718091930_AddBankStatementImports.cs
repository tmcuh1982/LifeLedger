using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MaskedIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    IdentifierHash = table.Column<string>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    LinkedAssetId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Assets_LinkedAssetId",
                        column: x => x.LinkedAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BankAccounts_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceFileName = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ImporterKey = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodStartsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PeriodEndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImports_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankStatementImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    BookedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Counterparty = table.Column<string>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "TEXT", nullable: true),
                    Classification = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    LinkedAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkedInvestmentPlanId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_Assets_LinkedAssetId",
                        column: x => x.LinkedAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankStatementImports_BankStatementImportId",
                        column: x => x.BankStatementImportId,
                        principalTable: "BankStatementImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankTransactions_Investments_LinkedInvestmentPlanId",
                        column: x => x.LinkedInvestmentPlanId,
                        principalTable: "Investments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_LinkedAssetId",
                table: "BankAccounts",
                column: "LinkedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_ScenarioId_IdentifierHash",
                table: "BankAccounts",
                columns: new[] { "ScenarioId", "IdentifierHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_BankAccountId_SourceFingerprint",
                table: "BankStatementImports",
                columns: new[] { "BankAccountId", "SourceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankStatementImportId_Fingerprint",
                table: "BankTransactions",
                columns: new[] { "BankStatementImportId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_LinkedAssetId",
                table: "BankTransactions",
                column: "LinkedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_LinkedInvestmentPlanId",
                table: "BankTransactions",
                column: "LinkedInvestmentPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "BankStatementImports");

            migrationBuilder.DropTable(
                name: "BankAccounts");
        }
    }
}
