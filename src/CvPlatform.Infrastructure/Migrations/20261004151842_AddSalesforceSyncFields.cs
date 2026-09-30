using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesforceSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "salesforce_account_id",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salesforce_contact_id",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "salesforce_account_id",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "salesforce_contact_id",
                table: "AspNetUsers");
        }
    }
}
