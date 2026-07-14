using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Yanban.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditValuesAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "new_value",
                table: "activity_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "old_value",
                table: "activity_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "activity_logs",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('russian', coalesce(summary, '')), 'A') || setweight(to_tsvector('russian', coalesce(old_value, '')), 'B') || setweight(to_tsvector('russian', coalesce(new_value, '')), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_search_vector",
                table: "activity_logs",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_logs_search_vector",
                table: "activity_logs");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "activity_logs");

            migrationBuilder.DropColumn(
                name: "new_value",
                table: "activity_logs");

            migrationBuilder.DropColumn(
                name: "old_value",
                table: "activity_logs");
        }
    }
}
