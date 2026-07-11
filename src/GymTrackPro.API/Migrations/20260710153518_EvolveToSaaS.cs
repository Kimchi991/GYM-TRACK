using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class EvolveToSaaS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "WalkInVisitors",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "Subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedBy",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "MembershipPlans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedBy",
                table: "MembershipPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "MembershipPlans",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MembershipPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "MembershipPauses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Members",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedBy",
                table: "Members",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "Members",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "UserID",
                table: "Members",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GymID",
                table: "AttendanceLogs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Gyms",
                columns: table => new
                {
                    GymID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContactNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CoverPhotoUrl = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OperatingHours = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Facebook = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Instagram = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amenities = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    BusinessPermitNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gyms", x => x.GymID);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
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
                    table.PrimaryKey("PK_PlatformSettings", x => x.SettingKey);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    PlanID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxMembers = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BillingCycleMonths = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.PlanID);
                });

            migrationBuilder.CreateTable(
                name: "GymInvitations",
                columns: table => new
                {
                    InvitationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GymID = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymInvitations", x => x.InvitationID);
                    table.ForeignKey(
                        name: "FK_GymInvitations_Gyms_GymID",
                        column: x => x.GymID,
                        principalTable: "Gyms",
                        principalColumn: "GymID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GymSettings",
                columns: table => new
                {
                    GymID = table.Column<int>(type: "int", nullable: false),
                    SettingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymSettings", x => new { x.GymID, x.SettingKey });
                    table.ForeignKey(
                        name: "FK_GymSettings_Gyms_GymID",
                        column: x => x.GymID,
                        principalTable: "Gyms",
                        principalColumn: "GymID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GymSubscriptions",
                columns: table => new
                {
                    SubscriptionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GymID = table.Column<int>(type: "int", nullable: false),
                    PlanID = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RenewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GymSubscriptions", x => x.SubscriptionID);
                    table.ForeignKey(
                        name: "FK_GymSubscriptions_Gyms_GymID",
                        column: x => x.GymID,
                        principalTable: "Gyms",
                        principalColumn: "GymID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GymSubscriptions_SubscriptionPlans_PlanID",
                        column: x => x.PlanID,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "PlanID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Gyms",
                columns: new[] { "GymID", "Address", "Amenities", "BusinessPermitNumber", "Capacity", "ContactNumber", "CoverPhotoUrl", "CreatedAt", "DeletedAt", "DeletedBy", "Description", "Email", "Facebook", "Instagram", "IsDeleted", "LogoUrl", "Name", "OperatingHours", "UpdatedAt", "Website" },
                values: new object[] { 1, "Main Street", null, null, 500, "+639170000000", null, new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, null, null, false, null, "Default Gym", null, new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "PlanID", "BillingCycleMonths", "Description", "MaxMembers", "Name", "Price" },
                values: new object[,]
                {
                    { 1, 1, "Standard multi-tenant plan.", 500, "Standard", 99.00m },
                    { 2, 1, "Premium multi-tenant plan.", 2000, "Premium", 199.00m }
                });

            migrationBuilder.InsertData(
                table: "GymSettings",
                columns: new[] { "GymID", "SettingKey", "Description", "GroupName", "LastModified", "SettingValue" },
                values: new object[,]
                {
                    { 1, "AllowedImageTypes", "Comma-separated list of approved image file extensions.", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), ".jpg,.jpeg,.png" },
                    { 1, "ContactNumber", "Gym contact helpline phone number.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "+639170000000" },
                    { 1, "Currency", "Currency code used for financial billing transactions.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "PHP" },
                    { 1, "GymName", "Name of the gym facility.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "GymTrackPro" },
                    { 1, "MaxUploadSize", "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "5242880" },
                    { 1, "PasswordPolicyRegex", "Regex pattern validating password strength rules.", "Security", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[@$!%*?&])[A-Za-z\\d@$!%*?&]{8,}$" },
                    { 1, "QRPrefix", "Format prefix added to automatically generated member QR codes.", "Membership", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "GTP-" },
                    { 1, "ReceiptPrefix", "Format prefix added to payment invoice transaction receipts.", "Payments", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "REC-" },
                    { 1, "ReminderDaysBeforeExpiration", "Days ahead of membership expiration to raise alerts or send reminders.", "Membership", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { 1, "Timezone", "System local timezone identifier.", "General", new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Asia/Manila" }
                });

            migrationBuilder.InsertData(
                table: "GymSubscriptions",
                columns: new[] { "SubscriptionID", "CancelledAt", "ExpiresAt", "GymID", "PlanID", "RenewedAt", "StartedAt", "Status", "TrialEndsAt" },
                values: new object[] { 1, null, new DateTime(2036, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), 1, 1, null, new DateTime(2026, 7, 2, 0, 0, 0, 0, DateTimeKind.Utc), 1, new DateTime(2026, 7, 16, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInVisitors_GymID",
                table: "WalkInVisitors",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_GymID",
                table: "Subscriptions",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GymID",
                table: "Payments",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_GymID",
                table: "Notifications",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_GymID",
                table: "MembershipPlans",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPauses_GymID",
                table: "MembershipPauses",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_Members_GymID",
                table: "Members",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_Members_UserID",
                table: "Members",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_GymID",
                table: "AttendanceLogs",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_GymInvitations_GymID",
                table: "GymInvitations",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_GymSubscriptions_GymID",
                table: "GymSubscriptions",
                column: "GymID");

            migrationBuilder.CreateIndex(
                name: "IX_GymSubscriptions_PlanID",
                table: "GymSubscriptions",
                column: "PlanID");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceLogs_Gyms_GymID",
                table: "AttendanceLogs",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Gyms_GymID",
                table: "Members",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Members_Users_UserID",
                table: "Members",
                column: "UserID",
                principalTable: "Users",
                principalColumn: "UserID");

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipPauses_Gyms_GymID",
                table: "MembershipPauses",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MembershipPlans_Gyms_GymID",
                table: "MembershipPlans",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Gyms_GymID",
                table: "Notifications",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Gyms_GymID",
                table: "Payments",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Gyms_GymID",
                table: "Subscriptions",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WalkInVisitors_Gyms_GymID",
                table: "WalkInVisitors",
                column: "GymID",
                principalTable: "Gyms",
                principalColumn: "GymID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceLogs_Gyms_GymID",
                table: "AttendanceLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_Gyms_GymID",
                table: "Members");

            migrationBuilder.DropForeignKey(
                name: "FK_Members_Users_UserID",
                table: "Members");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipPauses_Gyms_GymID",
                table: "MembershipPauses");

            migrationBuilder.DropForeignKey(
                name: "FK_MembershipPlans_Gyms_GymID",
                table: "MembershipPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Gyms_GymID",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Gyms_GymID",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Gyms_GymID",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_WalkInVisitors_Gyms_GymID",
                table: "WalkInVisitors");

            migrationBuilder.DropTable(
                name: "GymInvitations");

            migrationBuilder.DropTable(
                name: "GymSettings");

            migrationBuilder.DropTable(
                name: "GymSubscriptions");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "Gyms");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropIndex(
                name: "IX_WalkInVisitors_GymID",
                table: "WalkInVisitors");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_GymID",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_GymID",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_GymID",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_MembershipPlans_GymID",
                table: "MembershipPlans");

            migrationBuilder.DropIndex(
                name: "IX_MembershipPauses_GymID",
                table: "MembershipPauses");

            migrationBuilder.DropIndex(
                name: "IX_Members_GymID",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_Members_UserID",
                table: "Members");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceLogs_GymID",
                table: "AttendanceLogs");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "WalkInVisitors");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "MembershipPauses");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "UserID",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "GymID",
                table: "AttendanceLogs");

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    SettingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettingValue = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
    }
}
