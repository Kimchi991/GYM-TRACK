using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymTrackPro.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberApplications",
                columns: table => new
                {
                    ApplicationID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContactNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmergencyContact = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SelectedPlanID = table.Column<int>(type: "int", nullable: true),
                    IsOneDayPass = table.Column<bool>(type: "bit", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    PaymentReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    ApplicationStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedByUserID = table.Column<int>(type: "int", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberApplications", x => x.ApplicationID);
                    table.CheckConstraint("CK_MemberApplications_ExactlyOnePlanOrPass", "([IsOneDayPass] = 1 AND [SelectedPlanID] IS NULL) OR ([IsOneDayPass] = 0 AND [SelectedPlanID] IS NOT NULL)");
                    table.CheckConstraint("CK_MemberApplications_PaymentMethod", "[PaymentMethod] IN (0, 1, 2, 3, 4)");
                    table.CheckConstraint("CK_MemberApplications_PaymentStatus", "[PaymentStatus] IN (0, 1, 2, 3, 4, 5, 6, 7, 8)");
                    table.CheckConstraint("CK_MemberApplications_Status", "[ApplicationStatus] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_MemberApplications_MembershipPlans_SelectedPlanID",
                        column: x => x.SelectedPlanID,
                        principalTable: "MembershipPlans",
                        principalColumn: "PlanID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemberApplications_Users_VerifiedByUserID",
                        column: x => x.VerifiedByUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberApplications_SelectedPlanID",
                table: "MemberApplications",
                column: "SelectedPlanID");

            migrationBuilder.CreateIndex(
                name: "IX_MemberApplications_VerifiedByUserID",
                table: "MemberApplications",
                column: "VerifiedByUserID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberApplications");
        }
    }
}
