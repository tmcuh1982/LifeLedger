using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLedger.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNetWorthHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetWorthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NetWorth = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetWorthSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetWorthSnapshots_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetWorthSnapshots_ProfileId_CapturedAt",
                table: "NetWorthSnapshots",
                columns: new[] { "ProfileId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetWorthSnapshots");
        }
    }
}
