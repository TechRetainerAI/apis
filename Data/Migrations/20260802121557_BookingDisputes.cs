using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeDan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BookingDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeResolution",
                table: "Bookings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DisputeResolution",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "DisputedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Bookings");
        }
    }
}
