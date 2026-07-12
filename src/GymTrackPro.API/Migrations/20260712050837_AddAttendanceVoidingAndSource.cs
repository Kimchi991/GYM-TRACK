using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymTrackPro.API.Migrations
{
    /// <summary>
    /// Adds the capstone attendance correction model while preserving the original
    /// datetime2 attendance value for existing rows.
    /// </summary>
    public partial class AddAttendanceVoidingAndSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceLogs_Members_MemberID",
                table: "AttendanceLogs");

            migrationBuilder.RenameColumn(
                name: "AttendanceDate",
                table: "AttendanceLogs",
                newName: "AttendanceDateLegacyDateTime");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AttendanceDateLegacyDateTime",
                table: "AttendanceLogs",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<DateOnly>(
                name: "AttendanceDate",
                table: "AttendanceLogs",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [AttendanceLogs] " +
                "SET [AttendanceDate] = CONVERT(date, [AttendanceDateLegacyDateTime]) " +
                "WHERE [AttendanceDate] IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "AttendanceDate",
                table: "AttendanceLogs",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<int>("ActorUserID", "AttendanceLogs", "int", nullable: true);
            migrationBuilder.AddColumn<bool>("IsVoided", "AttendanceLogs", "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<byte[]>("RowVersion", "AttendanceLogs", "rowversion", rowVersion: true, nullable: false);
            migrationBuilder.AddColumn<string>("Source", "AttendanceLogs", "nvarchar(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<int>("SupersededByAttendanceID", "AttendanceLogs", "int", nullable: true);
            migrationBuilder.AddColumn<int>("VoidActorUserID", "AttendanceLogs", "int", nullable: true);
            migrationBuilder.AddColumn<string>("VoidReason", "AttendanceLogs", "nvarchar(255)", maxLength: 255, nullable: true);
            migrationBuilder.AddColumn<DateTime>("VoidedAtUtc", "AttendanceLogs", "datetime2", nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceLogs_CheckoutAfterCheckin",
                table: "AttendanceLogs",
                sql: "[IsVoided] = 1 OR [CheckOutTime] IS NULL OR [CheckOutTime] > [CheckInTime]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceLogs_VoidMetadata",
                table: "AttendanceLogs",
                sql: "([IsVoided] = 0 AND [VoidActorUserID] IS NULL AND [VoidedAtUtc] IS NULL AND [VoidReason] IS NULL) OR ([IsVoided] = 1 AND [VoidActorUserID] IS NOT NULL AND [VoidedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([VoidReason]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceLogs_NoSelfSupersession",
                table: "AttendanceLogs",
                sql: "[SupersededByAttendanceID] IS NULL OR [SupersededByAttendanceID] <> [AttendanceID]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceLogs_SupersessionRequiresVoid",
                table: "AttendanceLogs",
                sql: "[SupersededByAttendanceID] IS NULL OR [IsVoided] = 1");

            migrationBuilder.CreateTable(
                name: "AttendanceOperations",
                columns: table => new
                {
                    OperationID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserID = table.Column<int>(type: "int", nullable: false),
                    OperationType = table.Column<int>(type: "int", nullable: false),
                    RequestFingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    TargetAttendanceID = table.Column<int>(type: "int", nullable: true),
                    OriginalHttpStatus = table.Column<int>(type: "int", nullable: false),
                    OriginalResultCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceOperations", x => x.OperationID);
                    table.CheckConstraint(
                        "CK_AttendanceOperations_CompletionOrder",
                        "[CompletedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint(
                        "CK_AttendanceOperations_HttpStatusRange",
                        "[OriginalHttpStatus] BETWEEN 100 AND 599");
                    table.CheckConstraint(
                        "CK_AttendanceOperations_OperationType",
                        "[OperationType] IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint(
                        "CK_AttendanceOperations_ResultCodeNotBlank",
                        "LEN(LTRIM(RTRIM([OriginalResultCode]))) > 0");
                    table.CheckConstraint(
                        "CK_AttendanceOperations_State",
                        "[State] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_AttendanceOperations_AttendanceLogs_TargetAttendanceID",
                        column: x => x.TargetAttendanceID,
                        principalTable: "AttendanceLogs",
                        principalColumn: "AttendanceID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceOperations_Users_ActorUserID",
                        column: x => x.ActorUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceAdjustments",
                columns: table => new
                {
                    AttendanceAdjustmentID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceID = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BeforeCheckOutTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AfterCheckOutTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BeforeIsVoided = table.Column<bool>(type: "bit", nullable: true),
                    AfterIsVoided = table.Column<bool>(type: "bit", nullable: true),
                    BeforeSupersededByAttendanceID = table.Column<int>(type: "int", nullable: true),
                    AfterSupersededByAttendanceID = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ActorUserID = table.Column<int>(type: "int", nullable: false),
                    OperationID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAdjustments", x => x.AttendanceAdjustmentID);
                    table.CheckConstraint(
                        "CK_AttendanceAdjustments_Kind",
                        "[Kind] IN (0, 1, 2)");
                    table.CheckConstraint(
                        "CK_AttendanceAdjustments_ReasonNotBlank",
                        "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_AttendanceLogs_AttendanceID",
                        column: x => x.AttendanceID,
                        principalTable: "AttendanceLogs",
                        principalColumn: "AttendanceID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_AttendanceOperations_OperationID",
                        column: x => x.OperationID,
                        principalTable: "AttendanceOperations",
                        principalColumn: "OperationID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceAdjustments_Users_ActorUserID",
                        column: x => x.ActorUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.DropIndex("IX_AttendanceLogs_MemberID", "AttendanceLogs");

            migrationBuilder.CreateIndex("IX_AttendanceLogs_ActorUserID", "AttendanceLogs", "ActorUserID");
            migrationBuilder.CreateIndex("IX_AttendanceLogs_AttendanceDate", "AttendanceLogs", "AttendanceDate");
            migrationBuilder.CreateIndex("IX_AttendanceLogs_CheckInTime", "AttendanceLogs", "CheckInTime");
            migrationBuilder.CreateIndex(
                "IX_AttendanceLogs_MemberID_CheckInTime",
                "AttendanceLogs",
                new[] { "MemberID", "CheckInTime" });
            migrationBuilder.CreateIndex("IX_AttendanceLogs_SupersededByAttendanceID", "AttendanceLogs", "SupersededByAttendanceID");
            migrationBuilder.CreateIndex("IX_AttendanceLogs_VoidActorUserID", "AttendanceLogs", "VoidActorUserID");
            migrationBuilder.CreateIndex(
                "UX_AttendanceLogs_Member_AttendanceDate_NonVoided",
                "AttendanceLogs",
                new[] { "MemberID", "AttendanceDate" },
                unique: true,
                filter: "[IsVoided] = 0");
            migrationBuilder.CreateIndex(
                "UX_AttendanceLogs_Member_Open_NonVoided",
                "AttendanceLogs",
                "MemberID",
                unique: true,
                filter: "[CheckOutTime] IS NULL AND [IsVoided] = 0");
            migrationBuilder.CreateIndex("IX_AttendanceAdjustments_ActorUserID", "AttendanceAdjustments", "ActorUserID");
            migrationBuilder.CreateIndex("IX_AttendanceAdjustments_AttendanceID", "AttendanceAdjustments", "AttendanceID");
            migrationBuilder.CreateIndex("IX_AttendanceAdjustments_OperationID", "AttendanceAdjustments", "OperationID", unique: true);
            migrationBuilder.CreateIndex("IX_AttendanceOperations_ActorUserID", "AttendanceOperations", "ActorUserID");
            migrationBuilder.CreateIndex("IX_AttendanceOperations_TargetAttendanceID", "AttendanceOperations", "TargetAttendanceID");

            migrationBuilder.AddForeignKey(
                "FK_AttendanceLogs_Members_MemberID",
                "AttendanceLogs",
                "MemberID",
                "Members",
                principalColumn: "MemberID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                "FK_AttendanceLogs_AttendanceLogs_SupersededByAttendanceID",
                "AttendanceLogs",
                "SupersededByAttendanceID",
                "AttendanceLogs",
                principalColumn: "AttendanceID",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                "FK_AttendanceLogs_Users_ActorUserID",
                "AttendanceLogs",
                "ActorUserID",
                "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                "FK_AttendanceLogs_Users_VoidActorUserID",
                "AttendanceLogs",
                "VoidActorUserID",
                "Users",
                principalColumn: "UserID",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AttendanceAdjustments");
            migrationBuilder.DropTable(name: "AttendanceOperations");

            migrationBuilder.DropForeignKey("FK_AttendanceLogs_Members_MemberID", "AttendanceLogs");
            migrationBuilder.DropForeignKey("FK_AttendanceLogs_AttendanceLogs_SupersededByAttendanceID", "AttendanceLogs");
            migrationBuilder.DropForeignKey("FK_AttendanceLogs_Users_ActorUserID", "AttendanceLogs");
            migrationBuilder.DropForeignKey("FK_AttendanceLogs_Users_VoidActorUserID", "AttendanceLogs");

            migrationBuilder.DropIndex("IX_AttendanceLogs_ActorUserID", "AttendanceLogs");
            migrationBuilder.DropIndex("IX_AttendanceLogs_AttendanceDate", "AttendanceLogs");
            migrationBuilder.DropIndex("IX_AttendanceLogs_CheckInTime", "AttendanceLogs");
            migrationBuilder.DropIndex("IX_AttendanceLogs_MemberID_CheckInTime", "AttendanceLogs");
            migrationBuilder.DropIndex("IX_AttendanceLogs_SupersededByAttendanceID", "AttendanceLogs");
            migrationBuilder.DropIndex("IX_AttendanceLogs_VoidActorUserID", "AttendanceLogs");
            migrationBuilder.DropIndex("UX_AttendanceLogs_Member_AttendanceDate_NonVoided", "AttendanceLogs");
            migrationBuilder.DropIndex("UX_AttendanceLogs_Member_Open_NonVoided", "AttendanceLogs");

            migrationBuilder.DropCheckConstraint("CK_AttendanceLogs_CheckoutAfterCheckin", "AttendanceLogs");
            migrationBuilder.DropCheckConstraint("CK_AttendanceLogs_NoSelfSupersession", "AttendanceLogs");
            migrationBuilder.DropCheckConstraint("CK_AttendanceLogs_SupersessionRequiresVoid", "AttendanceLogs");
            migrationBuilder.DropCheckConstraint("CK_AttendanceLogs_VoidMetadata", "AttendanceLogs");

            migrationBuilder.DropColumn("ActorUserID", "AttendanceLogs");
            migrationBuilder.DropColumn("IsVoided", "AttendanceLogs");
            migrationBuilder.DropColumn("RowVersion", "AttendanceLogs");
            migrationBuilder.DropColumn("Source", "AttendanceLogs");
            migrationBuilder.DropColumn("SupersededByAttendanceID", "AttendanceLogs");
            migrationBuilder.DropColumn("VoidActorUserID", "AttendanceLogs");
            migrationBuilder.DropColumn("VoidReason", "AttendanceLogs");
            migrationBuilder.DropColumn("VoidedAtUtc", "AttendanceLogs");

            migrationBuilder.Sql(
                "UPDATE [AttendanceLogs] " +
                "SET [AttendanceDateLegacyDateTime] = CONVERT(datetime2, [AttendanceDate]) " +
                "WHERE [AttendanceDateLegacyDateTime] IS NULL;");

            migrationBuilder.DropColumn("AttendanceDate", "AttendanceLogs");

            migrationBuilder.AlterColumn<DateTime>(
                name: "AttendanceDateLegacyDateTime",
                table: "AttendanceLogs",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "AttendanceDateLegacyDateTime",
                table: "AttendanceLogs",
                newName: "AttendanceDate");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceLogs_MemberID",
                table: "AttendanceLogs",
                column: "MemberID");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceLogs_Members_MemberID",
                table: "AttendanceLogs",
                column: "MemberID",
                principalTable: "Members",
                principalColumn: "MemberID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
