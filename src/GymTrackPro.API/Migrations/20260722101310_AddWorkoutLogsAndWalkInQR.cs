using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutLogsAndWalkInQR : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "WalkInVisitors",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemporaryQRCode",
                table: "WalkInVisitors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkoutLogs",
                columns: table => new
                {
                    LogID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberID = table.Column<int>(type: "int", nullable: false),
                    TrainerUserID = table.Column<int>(type: "int", nullable: true),
                    RoutineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedExercisesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutLogs", x => x.LogID);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Members_MemberID",
                        column: x => x.MemberID,
                        principalTable: "Members",
                        principalColumn: "MemberID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Users_TrainerUserID",
                        column: x => x.TrainerUserID,
                        principalTable: "Users",
                        principalColumn: "UserID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_MemberID",
                table: "WorkoutLogs",
                column: "MemberID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_TrainerUserID",
                table: "WorkoutLogs",
                column: "TrainerUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "WalkInVisitors");

            migrationBuilder.DropColumn(
                name: "TemporaryQRCode",
                table: "WalkInVisitors");
        }
    }
}
