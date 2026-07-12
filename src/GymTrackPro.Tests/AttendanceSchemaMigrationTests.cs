using GymTrackPro.API.Data;
using GymTrackPro.API.Migrations;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Tests.AuthSecurity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;

namespace GymTrackPro.Tests;

public sealed class AttendanceSchemaMigrationTests
{
    [Fact]
    public void CurrentModelMigrationTargetAndSnapshot_PreserveCombinedIdentityAndAttendanceShape()
    {
        using var context = CreateContext();
        var models = new[]
        {
            context.GetService<IDesignTimeModel>().Model,
            new AddAttendanceVoidingAndSource().TargetModel,
            new GymDbContextModelSnapshot().Model
        };

        foreach (var model in models)
        {
            var attendance = model.FindEntityType(typeof(Attendance).FullName!);
            Assert.NotNull(attendance);
            Assert.Equal(typeof(DateOnly), attendance!.FindProperty(nameof(Attendance.AttendanceDate))!.ClrType);
            Assert.Equal("date", attendance.FindProperty(nameof(Attendance.AttendanceDate))!.GetColumnType());

            var legacyDate = attendance.FindProperty("AttendanceDateLegacyDateTime");
            Assert.NotNull(legacyDate);
            Assert.True(legacyDate!.IsNullable);
            Assert.Equal("datetime2", legacyDate.GetColumnType());

            var rowVersion = attendance.FindProperty(nameof(Attendance.RowVersion));
            Assert.NotNull(rowVersion);
            Assert.True(rowVersion!.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);

            var activeDailyVisit = attendance.GetIndexes().Single(index =>
                index.GetDatabaseName() == "UX_AttendanceLogs_Member_AttendanceDate_NonVoided");
            Assert.True(activeDailyVisit.IsUnique);
            Assert.Equal("[IsVoided] = 0", activeDailyVisit.GetFilter());
            Assert.Equal(
                new[] { nameof(Attendance.MemberID), nameof(Attendance.AttendanceDate) },
                activeDailyVisit.Properties.Select(property => property.Name).ToArray());

            var activeOpenSession = attendance.GetIndexes().Single(index =>
                index.GetDatabaseName() == "UX_AttendanceLogs_Member_Open_NonVoided");
            Assert.True(activeOpenSession.IsUnique);
            Assert.Equal("[CheckOutTime] IS NULL AND [IsVoided] = 0", activeOpenSession.GetFilter());

            var reportingIndexes = new Dictionary<string, string[]>
            {
                ["IX_AttendanceLogs_AttendanceDate"] = new[] { nameof(Attendance.AttendanceDate) },
                ["IX_AttendanceLogs_CheckInTime"] = new[] { nameof(Attendance.CheckInTime) },
                ["IX_AttendanceLogs_MemberID_CheckInTime"] = new[]
                {
                    nameof(Attendance.MemberID),
                    nameof(Attendance.CheckInTime)
                }
            };
            foreach (var expected in reportingIndexes)
            {
                var index = attendance.GetIndexes().Single(item =>
                    item.GetDatabaseName() == expected.Key);
                Assert.False(index.IsUnique);
                Assert.Null(index.GetFilter());
                Assert.Equal(
                    expected.Value,
                    index.Properties.Select(property => property.Name).ToArray());
            }

            var attendanceConstraints = attendance.GetCheckConstraints().ToList();
            Assert.Contains(attendanceConstraints, constraint =>
                constraint.Name == "CK_AttendanceLogs_CheckoutAfterCheckin"
                && constraint.Sql == "[IsVoided] = 1 OR [CheckOutTime] IS NULL OR [CheckOutTime] > [CheckInTime]");
            Assert.Contains(attendanceConstraints, constraint =>
                constraint.Name == "CK_AttendanceLogs_VoidMetadata"
                && constraint.Sql.Contains("[VoidActorUserID] IS NOT NULL", StringComparison.Ordinal)
                && constraint.Sql.Contains("LEN(LTRIM(RTRIM([VoidReason]))) > 0", StringComparison.Ordinal));
            Assert.Contains(attendanceConstraints, constraint =>
                constraint.Name == "CK_AttendanceLogs_NoSelfSupersession"
                && constraint.Sql.Contains(
                    "[SupersededByAttendanceID] <> [AttendanceID]",
                    StringComparison.Ordinal));
            Assert.Contains(attendanceConstraints, constraint =>
                constraint.Name == "CK_AttendanceLogs_SupersessionRequiresVoid"
                && constraint.Sql.Contains("[IsVoided] = 1", StringComparison.Ordinal));

            var operation = model.FindEntityType(typeof(AttendanceOperation).FullName!);
            Assert.NotNull(operation);
            Assert.Equal(
                "binary(32)",
                operation!.FindProperty(nameof(AttendanceOperation.RequestFingerprint))!.GetColumnType());
            Assert.All(operation.GetForeignKeys(), foreignKey =>
                Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
            var operationConstraints = GetConstraintSqlByName(operation);
            Assert.Equal("[OperationType] IN (0, 1, 2, 3, 4, 5)",
                operationConstraints["CK_AttendanceOperations_OperationType"]);
            Assert.Equal("[State] IN (0, 1)",
                operationConstraints["CK_AttendanceOperations_State"]);
            Assert.Equal("LEN(LTRIM(RTRIM([OriginalResultCode]))) > 0",
                operationConstraints["CK_AttendanceOperations_ResultCodeNotBlank"]);
            Assert.Equal("[OriginalHttpStatus] BETWEEN 100 AND 599",
                operationConstraints["CK_AttendanceOperations_HttpStatusRange"]);
            Assert.Equal("[CompletedAtUtc] >= [CreatedAtUtc]",
                operationConstraints["CK_AttendanceOperations_CompletionOrder"]);

            var adjustment = model.FindEntityType(typeof(AttendanceAdjustment).FullName!);
            Assert.NotNull(adjustment);
            Assert.All(adjustment!.GetForeignKeys(), foreignKey =>
                Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
            var adjustmentConstraints = GetConstraintSqlByName(adjustment);
            Assert.Equal("[Kind] IN (0, 1, 2)",
                adjustmentConstraints["CK_AttendanceAdjustments_Kind"]);
            Assert.Equal("LEN(LTRIM(RTRIM([Reason]))) > 0",
                adjustmentConstraints["CK_AttendanceAdjustments_ReasonNotBlank"]);

            var attendanceAuditLinks = attendance.GetForeignKeys().Where(foreignKey =>
                foreignKey.Properties.Any(property => property.Name is
                    nameof(Attendance.ActorUserID) or
                    nameof(Attendance.VoidActorUserID) or
                    nameof(Attendance.SupersededByAttendanceID)));
            Assert.All(attendanceAuditLinks, foreignKey =>
                Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
            var memberForeignKey = attendance.GetForeignKeys().Single(foreignKey =>
                foreignKey.Properties.Single().Name == nameof(Attendance.MemberID));
            Assert.Equal(DeleteBehavior.Restrict, memberForeignKey.DeleteBehavior);

            var invite = model.FindEntityType(typeof(AccountInvite).FullName!);
            Assert.NotNull(invite);
            Assert.Equal(
                "binary(32)",
                invite!.FindProperty(nameof(AccountInvite.TokenHash))!.GetColumnType());
        }
    }

    [Fact]
    public void AttendanceMigration_PreservesLegacyDateTimeAndBackfillsCalendarDate()
    {
        var root = TestWorkspace.FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Migrations",
            "20260712050837_AddAttendanceVoidingAndSource.cs"));

        Assert.Contains("newName: \"AttendanceDateLegacyDateTime\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "SET [AttendanceDate] = CONVERT(date, [AttendanceDateLegacyDateTime])",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AttendanceDateLegacyUtc", source, StringComparison.Ordinal);
        var rowVersionColumn = Regex.Match(
            source,
            "migrationBuilder\\.AddColumn<byte\\[\\]>\\s*\\((?<arguments>[\\s\\S]*?)\\);",
            RegexOptions.CultureInvariant);
        Assert.True(rowVersionColumn.Success);
        var rowVersionArguments = rowVersionColumn.Groups["arguments"].Value;
        Assert.Matches("(?:name:\\s*)?\"RowVersion\"", rowVersionArguments);
        Assert.Matches("(?:table:\\s*)?\"AttendanceLogs\"", rowVersionArguments);
        Assert.Matches("(?:type:\\s*)?\"rowversion\"", rowVersionArguments);
        Assert.Matches("rowVersion:\\s*true", rowVersionArguments);
        Assert.Matches("nullable:\\s*false", rowVersionArguments);
        Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
        Assert.Contains("UX_AttendanceLogs_Member_AttendanceDate_NonVoided", source, StringComparison.Ordinal);
        Assert.Contains("UX_AttendanceLogs_Member_Open_NonVoided", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceLogs_CheckoutAfterCheckin", source, StringComparison.Ordinal);
        Assert.Contains(
            "sql: \"[IsVoided] = 1 OR [CheckOutTime] IS NULL OR [CheckOutTime] > [CheckInTime]\"",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[CheckOutTime] >= [CheckInTime]", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceLogs_NoSelfSupersession", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceLogs_SupersessionRequiresVoid", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceLogs_VoidMetadata", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceOperations_OperationType", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceOperations_State", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceOperations_ResultCodeNotBlank", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceOperations_HttpStatusRange", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceOperations_CompletionOrder", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceAdjustments_Kind", source, StringComparison.Ordinal);
        Assert.Contains("CK_AttendanceAdjustments_ReasonNotBlank", source, StringComparison.Ordinal);
        Assert.Contains("IX_AttendanceLogs_AttendanceDate", source, StringComparison.Ordinal);
        Assert.Contains("IX_AttendanceLogs_CheckInTime", source, StringComparison.Ordinal);
        Assert.Contains("IX_AttendanceLogs_MemberID_CheckInTime", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttendanceMigration_CreatesUniqueIndexAndForeignKeyNames()
    {
        var root = TestWorkspace.FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Migrations",
            "20260712050837_AddAttendanceVoidingAndSource.cs"));
        var downStart = source.IndexOf(
            "protected override void Down",
            StringComparison.Ordinal);
        Assert.True(downStart > 0);
        var upSource = source[..downStart];
        var downSource = source[downStart..];

        var createdIndexNames = Regex.Matches(
                upSource,
                "(?<![A-Za-z])CreateIndex\\s*\\(\\s*(?:name:\\s*)?\"(?<name>[^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(
            createdIndexNames.Length,
            createdIndexNames.Distinct(StringComparer.Ordinal).Count());

        var createdForeignKeyNames = Regex.Matches(
                upSource,
                "(?<![A-Za-z])(?:AddForeignKey|ForeignKey)\\s*\\(\\s*(?:name:\\s*)?\"(?<name>FK_[^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(
            createdForeignKeyNames.Length,
            createdForeignKeyNames.Distinct(StringComparer.Ordinal).Count());

        var createdCheckConstraintNames = Regex.Matches(
                upSource,
                "(?<![A-Za-z])(?:AddCheckConstraint|CheckConstraint)\\s*\\(\\s*(?:name:\\s*)?\"(?<name>CK_[^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        Assert.Equal(
            createdCheckConstraintNames.Length,
            createdCheckConstraintNames.Distinct(StringComparer.Ordinal).Count());

        foreach (var indexName in new[]
                 {
                     "IX_AttendanceLogs_AttendanceDate",
                     "IX_AttendanceLogs_CheckInTime",
                     "IX_AttendanceLogs_MemberID_CheckInTime",
                     "UX_AttendanceLogs_Member_AttendanceDate_NonVoided",
                     "UX_AttendanceLogs_Member_Open_NonVoided"
                 })
        {
            Assert.Single(createdIndexNames, name => name == indexName);
        }

        Assert.Single(createdForeignKeyNames, name =>
            name == "FK_AttendanceLogs_Members_MemberID");
        foreach (var constraintName in new[]
                 {
                     "CK_AttendanceOperations_OperationType",
                     "CK_AttendanceOperations_State",
                     "CK_AttendanceOperations_ResultCodeNotBlank",
                     "CK_AttendanceOperations_HttpStatusRange",
                     "CK_AttendanceOperations_CompletionOrder",
                     "CK_AttendanceAdjustments_Kind",
                     "CK_AttendanceAdjustments_ReasonNotBlank"
                 })
        {
            Assert.Single(createdCheckConstraintNames, name => name == constraintName);
        }
        Assert.Contains("ReferentialAction.Restrict", upSource, StringComparison.Ordinal);
        Assert.Contains("ReferentialAction.Cascade", downSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AttendanceLedgerEnums_PreserveConstrainedOrdinals()
    {
        Assert.Equal(0, (int)AttendanceOperationType.StaffCheckIn);
        Assert.Equal(1, (int)AttendanceOperationType.StaffCheckOut);
        Assert.Equal(2, (int)AttendanceOperationType.GymGoerCheckOut);
        Assert.Equal(3, (int)AttendanceOperationType.CheckoutCorrection);
        Assert.Equal(4, (int)AttendanceOperationType.Void);
        Assert.Equal(5, (int)AttendanceOperationType.GymGoerCheckIn);
        Assert.Equal(0, (int)AttendanceOperationState.Completed);
        Assert.Equal(1, (int)AttendanceOperationState.Failed);
        Assert.Equal(0, (int)AttendanceAdjustmentKind.CheckoutCorrection);
        Assert.Equal(1, (int)AttendanceAdjustmentKind.Void);
        Assert.Equal(2, (int)AttendanceAdjustmentKind.Supersede);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GymTrackProAttendanceModelOnly;Trusted_Connection=True;")
            .Options;
        return new GymDbContext(options);
    }

    private static IReadOnlyDictionary<string, string> GetConstraintSqlByName(
        IEntityType entity)
    {
        var constraints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var constraint in entity.GetCheckConstraints())
        {
            var name = constraint.Name;
            Assert.NotNull(name);
            if (name is null)
            {
                continue;
            }

            constraints.Add(name, constraint.Sql);
        }

        return constraints;
    }
}
