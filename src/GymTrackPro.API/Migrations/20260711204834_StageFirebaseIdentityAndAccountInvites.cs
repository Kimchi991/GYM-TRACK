using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class StageFirebaseIdentityAndAccountInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "FirebaseUid",
                table: "Users",
                type: "nvarchar(128)",
                maxLength: 128,
                collation: "Latin1_General_100_BIN2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MemberID",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                collation: "Latin1_General_100_BIN2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_FirebaseUidNotBlank",
                table: "Users",
                sql: "[FirebaseUid] IS NULL OR LEN([FirebaseUid]) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_NormalizedEmailNotBlank",
                table: "Users",
                sql: "[NormalizedEmail] IS NULL OR LEN([NormalizedEmail]) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Role",
                table: "Users",
                sql: "[Role] IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users",
                sql: "([Role] = 2 AND [MemberID] IS NOT NULL) OR " +
                    "([Role] IN (0, 1) AND [MemberID] IS NULL)");

            migrationBuilder.CreateTable(
                name: "MemberProjectionVersions",
                columns: table => new
                {
                    MemberID = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberProjectionVersions", x => x.MemberID);
                    table.CheckConstraint(
                        "CK_MemberProjectionVersions_VersionRange",
                        "[Version] >= 0 AND [Version] <= 2199023255551");
                    table.ForeignKey(
                        name: "FK_MemberProjectionVersions_Members_MemberID",
                        column: x => x.MemberID,
                        principalTable: "Members",
                        principalColumn: "MemberID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "INSERT INTO [MemberProjectionVersions] ([MemberID], [Version]) " +
                "SELECT [MemberID], 0 FROM [Members];");

            migrationBuilder.Sql(
                "IF NOT EXISTS (SELECT 1 FROM [SystemSettings] WHERE [SettingKey] = N'StaleSessionHours') " +
                "INSERT INTO [SystemSettings] " +
                "([SettingKey], [SettingValue], [GroupName], [Description], [LastModified]) VALUES " +
                "(N'StaleSessionHours', N'16', N'Attendance', " +
                "N'Hours after which an open attendance session is considered stale.', " +
                "CAST('2026-07-02T00:00:00.0000000' AS datetime2));");

            migrationBuilder.CreateTable(
                name: "AccountInvites",
                columns: table => new
                {
                    AccountInviteID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetMemberID = table.Column<int>(type: "int", nullable: true),
                    TargetUserID = table.Column<int>(type: "int", nullable: true),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, collation: "Latin1_General_100_BIN2", nullable: false),
                    IntendedRole = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedByUserID = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedByFirebaseUid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, collation: "Latin1_General_100_BIN2", nullable: true),
                    RedemptionOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInvites", x => x.AccountInviteID);
                    table.CheckConstraint("CK_AccountInvites_ExactlyOneTarget", "([TargetMemberID] IS NOT NULL AND [TargetUserID] IS NULL) OR ([TargetMemberID] IS NULL AND [TargetUserID] IS NOT NULL)");
                    table.CheckConstraint("CK_AccountInvites_ExpiryAfterCreation", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_AccountInvites_NormalizedEmailNotBlank", "LEN([NormalizedEmail]) > 0");
                    table.CheckConstraint("CK_AccountInvites_PurposeNotBlank", "LEN(LTRIM(RTRIM([Purpose]))) > 0");
                    table.CheckConstraint("CK_AccountInvites_RevokedTimestampAfterCreation", "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_AccountInvites_TargetRole", "([TargetMemberID] IS NOT NULL AND [IntendedRole] = 2) OR ([TargetUserID] IS NOT NULL AND [IntendedRole] IN (0, 1))");
                    table.CheckConstraint("CK_AccountInvites_RedemptionMetadata", "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR ([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL AND [RedemptionOperationId] IS NOT NULL AND [RedemptionOperationId] <> CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))");
                    table.CheckConstraint("CK_AccountInvites_UsedOrRevoked", "[UsedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
                    table.CheckConstraint("CK_AccountInvites_UsedBeforeExpiry", "[UsedAtUtc] IS NULL OR [UsedAtUtc] < [ExpiresAtUtc]");
                    table.CheckConstraint("CK_AccountInvites_UsedTimestampAfterCreation", "[UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_AccountInvites_UsedUidNotBlank", "[UsedByFirebaseUid] IS NULL OR LEN([UsedByFirebaseUid]) > 0");
                    table.ForeignKey(
                        name: "FK_AccountInvites_Members_TargetMemberID",
                        column: x => x.TargetMemberID,
                        principalTable: "Members",
                        principalColumn: "MemberID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountInvites_Users_CreatedByUserID",
                        column: x => x.CreatedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountInvites_Users_TargetUserID",
                        column: x => x.TargetUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_FirebaseUid",
                table: "Users",
                column: "FirebaseUid",
                unique: true,
                filter: "[FirebaseUid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_MemberID",
                table: "Users",
                column: "MemberID",
                unique: true,
                filter: "[MemberID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_CreatedByUserID",
                table: "AccountInvites",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_NormalizedEmail",
                table: "AccountInvites",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_TargetMemberID",
                table: "AccountInvites",
                column: "TargetMemberID");

            migrationBuilder.CreateIndex(
                name: "UX_Users_FirebaseUid",
                table: "Users",
                column: "FirebaseUid",
                unique: true,
                filter: "[FirebaseUid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_MemberID",
                table: "Users",
                column: "MemberID",
                unique: true,
                filter: "[MemberID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_CreatedByUserID",
                table: "AccountInvites",
                column: "CreatedByUserID");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_NormalizedEmail",
                table: "AccountInvites",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_TargetMemberID",
                table: "AccountInvites",
                column: "TargetMemberID");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInvites_TargetUserID",
                table: "AccountInvites",
                column: "TargetUserID");

            migrationBuilder.CreateIndex(
                name: "UX_AccountInvites_RedemptionOperationId",
                table: "AccountInvites",
                column: "RedemptionOperationId",
                unique: true,
                filter: "[RedemptionOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AccountInvites_TokenHash",
                table: "AccountInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Members_MemberID",
                table: "Users",
                column: "MemberID",
                principalTable: "Members",
                principalColumn: "MemberID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Production rollback is forward-only: deploy a schema-compatible
            // compensating migration. This reverse path is for disposable
            // environments because it removes persisted invite history.
            // StaleSessionHours is intentionally retained: Up inserts it only
            // when absent, so Down cannot safely distinguish seeded state from
            // an operator-owned value.
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Members_MemberID",
                table: "Users");

            migrationBuilder.DropTable(
                name: "AccountInvites");

            migrationBuilder.DropTable(
                name: "MemberProjectionVersions");

            migrationBuilder.DropIndex(
                name: "UX_Users_NormalizedEmail",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Users_FirebaseUid",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Users_MemberID",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_FirebaseUidNotBlank",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_NormalizedEmailNotBlank",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Role",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirebaseUid",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MemberID",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }
    }
}
