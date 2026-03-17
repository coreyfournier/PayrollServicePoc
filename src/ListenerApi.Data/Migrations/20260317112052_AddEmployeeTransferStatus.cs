using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListenerApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeTransferStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeTransferStatuses",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CanTransfer = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PeriodCountLimitReached = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PeriodAmountLimitReached = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DailyLimitReached = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TransferCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmountTransferred = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DailyTransferCount = table.Column<int>(type: "int", nullable: false),
                    PeriodTransferLimit = table.Column<int>(type: "int", nullable: false),
                    PeriodAmountLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DailyTransferLimit = table.Column<int>(type: "int", nullable: false),
                    PayPeriodNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTransferStatuses", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_EmployeeTransferStatuses_EmployeeRecords_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "EmployeeRecords",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeTransferStatuses");
        }
    }
}
