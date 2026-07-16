using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileSex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Sex",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sex",
                table: "Profiles");
        }
    }
}
