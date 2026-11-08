using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Speranta.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdminExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "smtp_password_protected",
                table: "site_settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vapid_private_key_protected",
                table: "site_settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vapid_public_key",
                table: "site_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_alert_at",
                table: "mail_status",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "admin_invites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "mail_status",
                keyColumn: "id",
                keyValue: 1,
                column: "last_alert_at",
                value: null);

            migrationBuilder.UpdateData(
                table: "site_settings",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "smtp_password_protected", "vapid_private_key_protected", "vapid_public_key" },
                values: new object[] { null, null, null });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "smtp_password_protected",
                table: "site_settings");

            migrationBuilder.DropColumn(
                name: "vapid_private_key_protected",
                table: "site_settings");

            migrationBuilder.DropColumn(
                name: "vapid_public_key",
                table: "site_settings");

            migrationBuilder.DropColumn(
                name: "last_alert_at",
                table: "mail_status");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "admin_invites");
        }
    }
}
