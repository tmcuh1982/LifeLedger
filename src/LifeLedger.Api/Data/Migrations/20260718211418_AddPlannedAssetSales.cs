using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedAssetSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HappensOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UseProjectedValue = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrossSalePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SellingCosts = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CapitalGainsTaxRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CapitalGainsTaxCountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    RepayLinkedLiabilities = table.Column<bool>(type: "INTEGER", nullable: false),
                    Destination = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DestinationInvestmentPlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetSales_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetSales_Assets_DestinationAssetId",
                        column: x => x.DestinationAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetSales_Investments_DestinationInvestmentPlanId",
                        column: x => x.DestinationInvestmentPlanId,
                        principalTable: "Investments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssetSales_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetSales_AssetId",
                table: "AssetSales",
                column: "AssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetSales_DestinationAssetId",
                table: "AssetSales",
                column: "DestinationAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSales_DestinationInvestmentPlanId",
                table: "AssetSales",
                column: "DestinationInvestmentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSales_ScenarioId_HappensOn",
                table: "AssetSales",
                columns: new[] { "ScenarioId", "HappensOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetSales");
        }
    }
}
