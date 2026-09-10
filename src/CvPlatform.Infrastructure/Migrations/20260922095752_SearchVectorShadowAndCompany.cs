using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace CvPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchVectorShadowAndCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "positions",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "title", "short_description", "company" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "title", "short_description" });

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE dup_count integer;
                BEGIN
                  SELECT COUNT(*) INTO dup_count FROM (
                    SELECT lower("name")
                    FROM "attribute_definitions"
                    GROUP BY lower("name")
                    HAVING COUNT(*) > 1
                  ) d;
                  IF dup_count > 0 THEN
                    RAISE EXCEPTION 'Cannot create unique index ix_attribute_definitions_lower_name: % case-insensitive duplicate attribute name(s) exist. Resolve duplicates before migrating.', dup_count;
                  END IF;
                END $$;
                CREATE UNIQUE INDEX IF NOT EXISTS "ix_attribute_definitions_lower_name"
                    ON "attribute_definitions" (lower("name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"ix_attribute_definitions_lower_name\"");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "positions",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "title", "short_description" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "title", "short_description", "company" });
        }
    }
}
