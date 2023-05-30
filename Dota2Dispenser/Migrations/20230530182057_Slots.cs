using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dota2Dispenser.Migrations
{
    /// <inheritdoc />
    public partial class Slots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayerSlot",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamNumber",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamSlot",
                table: "Players",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .OldAnnotation("Sqlite:Autoincrement", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayerSlot",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TeamNumber",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "TeamSlot",
                table: "Players");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER")
                .Annotation("Sqlite:Autoincrement", true);
        }
    }
}
