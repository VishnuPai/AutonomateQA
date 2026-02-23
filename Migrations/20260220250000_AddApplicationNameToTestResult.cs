using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UiTestRunner.Migrations
{
    /// <inheritdoc />
    [Migration("20260220250000_AddApplicationNameToTestResult")]
    public partial class AddApplicationNameToTestResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "TestResults",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ApplicationName", table: "TestResults");
        }
    }
}
