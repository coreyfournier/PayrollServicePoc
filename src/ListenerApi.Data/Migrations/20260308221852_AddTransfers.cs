using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListenerApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TransferCount",
                table: "EmployeePayAttributes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferTotalAmount",
                table: "EmployeePayAttributes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "TransferRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PayPeriodNumber = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitiatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailureReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalReferenceId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferRecords_EmployeeRecords_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "EmployeeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRecords_EmployeeId_InitiatedAt",
                table: "TransferRecords",
                columns: new[] { "EmployeeId", "InitiatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRecords_EmployeeId_PayPeriodNumber",
                table: "TransferRecords",
                columns: new[] { "EmployeeId", "PayPeriodNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferRecords");

            migrationBuilder.DropColumn(
                name: "TransferCount",
                table: "EmployeePayAttributes");

            migrationBuilder.DropColumn(
                name: "TransferTotalAmount",
                table: "EmployeePayAttributes");
        }
    }
}
