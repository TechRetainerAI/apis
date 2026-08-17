using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeDan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReferralsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Users_RefereeUserId",
                table: "Referrals");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_RefereeUserId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferrerUserId",
                table: "Referrals");

            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // NEWID() (not a constant): existing rows each need a distinct key or the
            // primary key below can't be created.
            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Referrals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QualifyingBookingId",
                table: "Referrals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefereeName",
                table: "Referrals",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ReferralCode",
                table: "Users",
                column: "ReferralCode",
                unique: true,
                filter: "[ReferralCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_Code",
                table: "Referrals",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_RefereeUserId",
                table: "Referrals",
                column: "RefereeUserId",
                unique: true,
                filter: "[RefereeUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerUserId_CreatedAt",
                table: "Referrals",
                columns: new[] { "ReferrerUserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Users_RefereeUserId",
                table: "Referrals",
                column: "RefereeUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Users_RefereeUserId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Users_ReferralCode",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_Code",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_RefereeUserId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ReferrerUserId_CreatedAt",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "QualifyingBookingId",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "RefereeName",
                table: "Referrals");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Referrals",
                table: "Referrals",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_RefereeUserId",
                table: "Referrals",
                column: "RefereeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerUserId",
                table: "Referrals",
                column: "ReferrerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Users_RefereeUserId",
                table: "Referrals",
                column: "RefereeUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
