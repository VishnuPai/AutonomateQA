using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UiTestRunner.Migrations
{
    /// <inheritdoc />
    [Migration("20260220210000_AddTokenUsageToTestResult")]
    public partial class AddTokenUsageToTestResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PromptTokens",
                table: "TestResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompletionTokens",
                table: "TestResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "TestResults",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PromptTokens", table: "TestResults");
            migrationBuilder.DropColumn(name: "CompletionTokens", table: "TestResults");
            migrationBuilder.DropColumn(name: "TotalTokens", table: "TestResults");
        }
    }
}
