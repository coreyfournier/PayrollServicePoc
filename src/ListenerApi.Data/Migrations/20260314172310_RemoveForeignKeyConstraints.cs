using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListenerApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveForeignKeyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK constraints entirely — this is an eventually-consistent read model
            // where events arrive out of order from different Kafka partitions.
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePayAttributes_EmployeeRecords_EmployeeId",
                table: "EmployeePayAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_TransferRecords_EmployeeRecords_EmployeeId",
                table: "TransferRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePayAttributes_EmployeeRecords_EmployeeId",
                table: "EmployeePayAttributes");

            migrationBuilder.DropForeignKey(
                name: "FK_TransferRecords_EmployeeRecords_EmployeeId",
                table: "TransferRecords");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePayAttributes_EmployeeRecords_EmployeeId",
                table: "EmployeePayAttributes",
                column: "EmployeeId",
                principalTable: "EmployeeRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TransferRecords_EmployeeRecords_EmployeeId",
                table: "TransferRecords",
                column: "EmployeeId",
                principalTable: "EmployeeRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
