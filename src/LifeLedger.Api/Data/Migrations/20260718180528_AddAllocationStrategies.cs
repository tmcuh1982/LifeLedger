using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInPortfolioAllocation",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "AllocationStrategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationStrategies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationStrategies_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AllocationStrategyTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllocationStrategyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TargetPercentage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TolerancePercentage = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationStrategyTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationStrategyTargets_AllocationStrategies_AllocationStrategyId",
                        column: x => x.AllocationStrategyId,
                        principalTable: "AllocationStrategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationStrategies_ScenarioId_EffectiveFrom",
                table: "AllocationStrategies",
                columns: new[] { "ScenarioId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationStrategyTargets_AllocationStrategyId_Category",
                table: "AllocationStrategyTargets",
                columns: new[] { "AllocationStrategyId", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationStrategyTargets");

            migrationBuilder.DropTable(
                name: "AllocationStrategies");

            migrationBuilder.DropColumn(
                name: "IsIncludedInPortfolioAllocation",
                table: "Assets");
        }
    }
}
