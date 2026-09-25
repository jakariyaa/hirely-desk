using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImageObjectKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "image_url",
                table: "profile_attribute_values",
                newName: "image_object_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "image_object_key",
                table: "profile_attribute_values",
                newName: "image_url");
        }
    }
}
