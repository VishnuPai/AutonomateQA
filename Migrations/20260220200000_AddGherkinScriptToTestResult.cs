using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UiTestRunner.Migrations
{
    /// <inheritdoc />
    [Migration("20260220200000_AddGherkinScriptToTestResult")]
    public partial class AddGherkinScriptToTestResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GherkinScript",
                table: "TestResults",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GherkinScript",
                table: "TestResults");
        }
    }
}
