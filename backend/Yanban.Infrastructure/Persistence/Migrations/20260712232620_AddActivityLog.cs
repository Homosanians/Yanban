using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Yanban.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_logs", x => x.id);
                    table.UniqueConstraint("ak_activity_logs_sequence", x => x.sequence);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_board_id_sequence",
                table: "activity_logs",
                columns: new[] { "board_id", "sequence" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs");
        }
    }
}
