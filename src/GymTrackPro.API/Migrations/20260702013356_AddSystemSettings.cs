using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    SettingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.SettingKey);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "SettingKey", "Description", "GroupName", "LastModified", "SettingValue" },
                values: new object[,]
                {
                    { "AllowedImageTypes", "Comma-separated list of approved image file extensions.", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), ".jpg,.jpeg,.png" },
                    { "ContactNumber", "Gym contact helpline phone number.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "+639170000000" },
                    { "Currency", "Currency code used for financial billing transactions.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "PHP" },
                    { "GymName", "Name of the gym facility.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "GymTrackPro" },
                    { "MaxUploadSize", "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "5242880" },
                    { "PasswordPolicyRegex", "Regex pattern validating password strength rules.", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[@$!%*?&])[A-Za-z\\d@$!%*?&]{8,}$" },
                    { "QRPrefix", "Format prefix added to automatically generated member QR codes.", "Membership", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "GTP-" },
                    { "ReceiptPrefix", "Format prefix added to payment invoice transaction receipts.", "Payments", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "REC-" },
                    { "ReminderDaysBeforeExpiration", "Days ahead of membership expiration to raise alerts or send reminders.", "Membership", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { "Timezone", "System local timezone identifier.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Asia/Manila" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
