using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Speranta.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerBookForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allows_pickup",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "asks_age_group",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "asks_bible_at_home",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_for_children",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "has_bible_at_home",
                table: "book_orders",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "age_group",
                table: "book_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { false, true, true, false });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { false, true, false, false });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 3,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { false, true, false, false });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 4,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { false, true, false, false });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 5,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { true, false, false, true });

            migrationBuilder.UpdateData(
                table: "books",
                keyColumn: "id",
                keyValue: 6,
                columns: new[] { "allows_pickup", "asks_age_group", "asks_bible_at_home", "is_for_children" },
                values: new object[] { true, false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allows_pickup",
                table: "books");

            migrationBuilder.DropColumn(
                name: "asks_age_group",
                table: "books");

            migrationBuilder.DropColumn(
                name: "asks_bible_at_home",
                table: "books");

            migrationBuilder.DropColumn(
                name: "is_for_children",
                table: "books");

            migrationBuilder.AlterColumn<bool>(
                name: "has_bible_at_home",
                table: "book_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "age_group",
                table: "book_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
