using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAssetIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_ScenarioId",
                table: "Assets");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Assets",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                table: "Assets",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ScenarioId_ExternalProvider_ExternalId",
                table: "Assets",
                columns: new[] { "ScenarioId", "ExternalProvider", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_ScenarioId_ExternalProvider_ExternalId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ScenarioId",
                table: "Assets",
                column: "ScenarioId");
        }
    }
}
