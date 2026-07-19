using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObservedBankExpenseLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_ScenarioId",
                table: "Expenses");

            migrationBuilder.AddColumn<string>(
                name: "ObservedBankCategory",
                table: "Expenses",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ScenarioId_ObservedBankCategory_Currency",
                table: "Expenses",
                columns: new[] { "ScenarioId", "ObservedBankCategory", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_ScenarioId_ObservedBankCategory_Currency",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ObservedBankCategory",
                table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ScenarioId",
                table: "Expenses",
                column: "ScenarioId");
        }
    }
}
