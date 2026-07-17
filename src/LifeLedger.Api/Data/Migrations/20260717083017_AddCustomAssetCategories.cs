using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomAssetCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomCategory",
                table: "Assets",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomCategory",
                table: "Assets");
        }
    }
}
