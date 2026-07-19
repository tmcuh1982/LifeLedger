using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonalIncomeAndAssetLinkedCashflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AmountMode",
                table: "Incomes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualAmount",
                table: "Incomes",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedAssetId",
                table: "Incomes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedAssetId",
                table: "Expenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncomeMonthlyAllocations",
                columns: table => new
                {
                    IncomeStreamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Share = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeMonthlyAllocations", x => new { x.IncomeStreamId, x.Month });
                    table.ForeignKey(
                        name: "FK_IncomeMonthlyAllocations_Incomes_IncomeStreamId",
                        column: x => x.IncomeStreamId,
                        principalTable: "Incomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incomes_LinkedAssetId",
                table: "Incomes",
                column: "LinkedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_LinkedAssetId",
                table: "Expenses",
                column: "LinkedAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Assets_LinkedAssetId",
                table: "Expenses",
                column: "LinkedAssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_Assets_LinkedAssetId",
                table: "Incomes",
                column: "LinkedAssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Assets_LinkedAssetId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_Assets_LinkedAssetId",
                table: "Incomes");

            migrationBuilder.DropTable(
                name: "IncomeMonthlyAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Incomes_LinkedAssetId",
                table: "Incomes");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_LinkedAssetId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "AmountMode",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "AnnualAmount",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "LinkedAssetId",
                table: "Incomes");

            migrationBuilder.DropColumn(
                name: "LinkedAssetId",
                table: "Expenses");
        }
    }
}
