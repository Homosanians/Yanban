using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yanban.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityNotifyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Doorbell for the realtime tailer. pg_notify runs on insert but only reaches
            // listeners when the transaction commits, so a rolled-back mutation never wakes
            // anyone. The payload is empty: it is a wake signal, and the tailer reads the rows
            // from the log itself. Channel name must match PostgresActivityListener.Channel.
            // Statement-level fires once per write regardless of row count, which is all a
            // doorbell needs.
            migrationBuilder.Sql("""
                CREATE FUNCTION notify_activity() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('yanban_activity', '');
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_activity_notify
                AFTER INSERT ON activity_logs
                FOR EACH STATEMENT
                EXECUTE FUNCTION notify_activity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_activity_notify ON activity_logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS notify_activity();");
        }
    }
}
