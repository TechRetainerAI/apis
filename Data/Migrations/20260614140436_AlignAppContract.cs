using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeDan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignAppContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Campuses",
                keyColumn: "Code",
                keyValue: "USTED",
                columns: new[] { "City", "FullName", "Latitude", "Longitude" },
                values: new object[] { "Kumasi-Tanoso", "AAMUSTED", 6.6985000000000001, -1.6244000000000001 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Campuses",
                keyColumn: "Code",
                keyValue: "USTED",
                columns: new[] { "City", "FullName", "Latitude", "Longitude" },
                values: new object[] { "Sunyani", "University of Science, Technology, Engineering & Development", 7.3360000000000003, -2.3359999999999999 });
        }
    }
}
