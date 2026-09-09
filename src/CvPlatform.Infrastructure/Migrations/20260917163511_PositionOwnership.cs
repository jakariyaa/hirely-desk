using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PositionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_owner_id",
                table: "positions",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_positions_users_owner_id",
                table: "positions",
                column: "owner_id",
                principalTable: "AspNetUsers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_positions_users_owner_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_owner_id",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "positions");
        }
    }
}
