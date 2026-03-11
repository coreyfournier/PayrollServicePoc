using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListenerApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxCleanupEvent : Migration
    {
        /// <summary>
        /// Creates a MySQL scheduled event to purge OutboxMessages older than 2 hours.
        ///
        /// This is safe because Debezium reads from the MySQL binlog, not the
        /// OutboxMessages table. Once a row is INSERTed, the INSERT is permanently
        /// recorded in the binlog. Debezium tracks its offset in the binlog stream,
        /// so rows can be deleted from the table without affecting message delivery.
        /// The 2-hour retention is a convenience window for debugging/observability.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages",
                column: "CreatedAt");

            migrationBuilder.Sql(@"
                DROP EVENT IF EXISTS cleanup_outbox_messages;
                CREATE EVENT cleanup_outbox_messages
                    ON SCHEDULE EVERY 2 HOUR
                    DO DELETE FROM OutboxMessages WHERE CreatedAt < NOW() - INTERVAL 2 HOUR;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP EVENT IF EXISTS cleanup_outbox_messages;");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages");
        }
    }
}
