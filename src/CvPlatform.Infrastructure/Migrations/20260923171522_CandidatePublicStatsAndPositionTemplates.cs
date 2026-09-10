using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CandidatePublicStatsAndPositionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "cvs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE cvs SET created_at = COALESCE(published_at, CURRENT_TIMESTAMP) WHERE created_at IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "cvs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "position_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    max_projects = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_templates_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_template_access_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_type = table.Column<string>(type: "text", nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "text", nullable: false),
                    comparison_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    numeric_comparison = table.Column<decimal>(type: "numeric", nullable: true),
                    date_comparison = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_template_access_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_template_access_rules_attribute_definitions_attrib",
                        column: x => x.attribute_definition_id,
                        principalTable: "attribute_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_template_access_rules_position_templates_position_",
                        column: x => x.position_template_id,
                        principalTable: "position_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "position_template_attributes",
                columns: table => new
                {
                    position_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_template_attributes", x => new { x.position_template_id, x.attribute_definition_id });
                    table.ForeignKey(
                        name: "fk_position_template_attributes_attribute_definitions_attribut",
                        column: x => x.attribute_definition_id,
                        principalTable: "attribute_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_template_attributes_position_templates_position_te",
                        column: x => x.position_template_id,
                        principalTable: "position_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cvs_created_at",
                table: "cvs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_position_template_access_rules_attribute_definition_id",
                table: "position_template_access_rules",
                column: "attribute_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_template_access_rules_position_template_id_attribu",
                table: "position_template_access_rules",
                columns: new[] { "position_template_id", "attribute_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_template_attributes_attribute_definition_id",
                table: "position_template_attributes",
                column: "attribute_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_templates_created_by_id",
                table: "position_templates",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_templates_is_active_name",
                table: "position_templates",
                columns: new[] { "is_active", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_template_access_rules");

            migrationBuilder.DropTable(
                name: "position_template_attributes");

            migrationBuilder.DropTable(
                name: "position_templates");

            migrationBuilder.DropIndex(
                name: "ix_cvs_created_at",
                table: "cvs");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "cvs");
        }
    }
}
