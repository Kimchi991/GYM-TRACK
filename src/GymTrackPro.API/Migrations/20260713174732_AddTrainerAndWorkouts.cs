using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerAndWorkouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Role",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "TrainerClients",
                columns: table => new
                {
                    TrainerClientID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrainerUserID = table.Column<int>(type: "int", nullable: false),
                    MemberID = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerClients", x => x.TrainerClientID);
                    table.ForeignKey(
                        name: "FK_TrainerClients_Members_MemberID",
                        column: x => x.MemberID,
                        principalTable: "Members",
                        principalColumn: "MemberID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrainerClients_Users_TrainerUserID",
                        column: x => x.TrainerUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutRoutines",
                columns: table => new
                {
                    RoutineID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberID = table.Column<int>(type: "int", nullable: false),
                    TrainerUserID = table.Column<int>(type: "int", nullable: false),
                    RoutineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExercisesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutRoutines", x => x.RoutineID);
                    table.ForeignKey(
                        name: "FK_WorkoutRoutines_Members_MemberID",
                        column: x => x.MemberID,
                        principalTable: "Members",
                        principalColumn: "MemberID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkoutRoutines_Users_TrainerUserID",
                        column: x => x.TrainerUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Role",
                table: "Users",
                sql: "[Role] IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users",
                sql: "([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1, 3) AND [MemberID] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerClients_MemberID",
                table: "TrainerClients",
                column: "MemberID");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerClients_TrainerUserID",
                table: "TrainerClients",
                column: "TrainerUserID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutRoutines_MemberID",
                table: "WorkoutRoutines",
                column: "MemberID");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutRoutines_TrainerUserID",
                table: "WorkoutRoutines",
                column: "TrainerUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrainerClients");

            migrationBuilder.DropTable(
                name: "WorkoutRoutines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Role",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Role",
                table: "Users",
                sql: "[Role] IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_RoleMemberLink",
                table: "Users",
                sql: "([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1) AND [MemberID] IS NULL)");
        }
    }
}
