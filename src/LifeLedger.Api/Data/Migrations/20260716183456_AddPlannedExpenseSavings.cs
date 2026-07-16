using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedExpenseSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SaveInAdvance",
                table: "Expenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SavingsStartsOn",
                table: "Expenses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SaveInAdvance",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "SavingsStartsOn",
                table: "Expenses");
        }
    }
}
