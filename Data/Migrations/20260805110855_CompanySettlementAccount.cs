using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeDan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompanySettlementAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaystackRecipientCode",
                table: "Companies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementAccountName",
                table: "Companies",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementAccountNumber",
                table: "Companies",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementBankCode",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementUpdatedAt",
                table: "Companies",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaystackRecipientCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SettlementAccountName",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SettlementAccountNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SettlementBankCode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SettlementUpdatedAt",
                table: "Companies");
        }
    }
}
