using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusTicketing.Data.Migrations
{
    /// <inheritdoc />
    public partial class SmsLogAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_base_url",
                table: "app_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sms_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(640)", maxLength: 640, nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent = table.Column<bool>(type: "boolean", nullable: false),
                    provider_response = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sms_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sms_logs_booking_id",
                table: "sms_logs",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_sms_logs_created_at",
                table: "sms_logs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sms_logs");

            migrationBuilder.DropColumn(
                name: "public_base_url",
                table: "app_settings");
        }
    }
}
