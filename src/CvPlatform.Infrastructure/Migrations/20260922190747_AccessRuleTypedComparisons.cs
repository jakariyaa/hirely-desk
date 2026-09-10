using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccessRuleTypedComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "date_comparison",
                table: "access_rules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "numeric_comparison",
                table: "access_rules",
                type: "numeric",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "access_rules" SET "numeric_comparison" = "comparison_value"::numeric
                WHERE "data_type" = 'Numeric'
                  AND "comparison_value" ~ '^-?[0-9]+(\.[0-9]+)?$';
                """);
            migrationBuilder.Sql(
                """
                UPDATE "access_rules" SET "date_comparison" = "comparison_value"::date
                WHERE "data_type" IN ('Date', 'Period')
                  AND "comparison_value" ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "date_comparison",
                table: "access_rules");

            migrationBuilder.DropColumn(
                name: "numeric_comparison",
                table: "access_rules");
        }
    }
}
