using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Yanban.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplatesAndObjectDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "object_deletions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_object_deletions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_object_deletions_pending",
                table: "object_deletions",
                column: "enqueued_at",
                filter: "deleted_at IS NULL");

            // The heart of the storage GC (ADR-18). Every attachment row that dies — by app delete,
            // by cascade from a card/list/board, or by a manual DELETE in psql — enqueues its object
            // for the worker to remove from S3, in the *same transaction* as the delete. Because it
            // is the database doing the cascade, it must be the database doing the enqueue: nothing
            // in the application ever sees a cascaded row.
            //
            // FOR EACH ROW, so a bulk cascade (a board with hundreds of attachments) enqueues each
            // one. OLD is the row being deleted, still fully populated here.
            migrationBuilder.Sql("""
                CREATE FUNCTION enqueue_object_deletion() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO object_deletions (storage_key, enqueued_at, attempts)
                    VALUES (OLD.storage_key, now(), 0);
                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_attachment_deleted
                AFTER DELETE ON attachments
                FOR EACH ROW
                EXECUTE FUNCTION enqueue_object_deletion();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_attachment_deleted ON attachments;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS enqueue_object_deletion();");
            migrationBuilder.DropTable(
                name: "object_deletions");
        }
    }
}
