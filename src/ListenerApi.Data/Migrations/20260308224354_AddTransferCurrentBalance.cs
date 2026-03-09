using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListenerApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCurrentBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBalance",
                table: "TransferRecords",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentBalance",
                table: "TransferRecords");
        }
    }
}
