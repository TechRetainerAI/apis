using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeDan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManualProofSetAndBroadcastLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProofKeys",
                table: "Payments",
                type: "nvarchar(700)",
                maxLength: 700,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationBroadcasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Recipients = table.Column<int>(type: "int", nullable: false),
                    Devices = table.Column<int>(type: "int", nullable: false),
                    Delivered = table.Column<bool>(type: "bit", nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationBroadcasts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationBroadcasts");

            migrationBuilder.DropColumn(
                name: "ProofKeys",
                table: "Payments");
        }
    }
}
