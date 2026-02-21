using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UiTestRunner.Migrations
{
    /// <inheritdoc />
    public partial class AddHangfireJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                table: "TestResults",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                table: "TestResults");
        }
    }
}
