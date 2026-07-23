using Microsoft.EntityFrameworkCore;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Data;

public class GymDbContext : DbContext
{
    public GymDbContext(DbContextOptions<GymDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<AccountInvite> AccountInvites => Set<AccountInvite>();
    public DbSet<MemberProjectionVersion> MemberProjectionVersions => Set<MemberProjectionVersion>();
    public DbSet<AttendanceOperation> AttendanceOperations => Set<AttendanceOperation>();
    public DbSet<AttendanceAdjustment> AttendanceAdjustments => Set<AttendanceAdjustment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<MembershipPause> MembershipPauses => Set<MembershipPause>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Attendance> AttendanceLogs => Set<Attendance>();
    public DbSet<WalkInVisitor> WalkInVisitors => Set<WalkInVisitor>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<MemberApplication> MemberApplications => Set<MemberApplication>();
    public DbSet<TrainerClient> TrainerClients => Set<TrainerClient>();
    public DbSet<WorkoutRoutine> WorkoutRoutines => Set<WorkoutRoutine>();
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique constraints
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.FirebaseUid)
            .UseCollation("Latin1_General_100_BIN2");

        modelBuilder.Entity<User>()
            .Property(u => u.NormalizedEmail)
            .UseCollation("Latin1_General_100_BIN2");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.FirebaseUid)
            .IsUnique()
            .HasDatabaseName("UX_Users_FirebaseUid")
            .HasFilter("[FirebaseUid] IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("UX_Users_NormalizedEmail")
            .HasFilter("[NormalizedEmail] IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.MemberID)
            .IsUnique()
            .HasDatabaseName("UX_Users_MemberID")
            .HasFilter("[MemberID] IS NOT NULL");

        modelBuilder.Entity<User>()
            .ToTable("Users", table =>
            {
                table.HasCheckConstraint(
                    "CK_Users_FirebaseUidNotBlank",
                    "[FirebaseUid] IS NULL OR LEN([FirebaseUid]) > 0");
                table.HasCheckConstraint(
                    "CK_Users_NormalizedEmailNotBlank",
                    "[NormalizedEmail] IS NULL OR LEN([NormalizedEmail]) > 0");
                table.HasCheckConstraint("CK_Users_Role", "[Role] IN (0, 1, 2, 3)");
                table.HasCheckConstraint(
                    "CK_Users_RoleMemberLink",
                    "([Role] = 2 AND [MemberID] IS NOT NULL) OR ([Role] IN (0, 1, 3) AND [MemberID] IS NULL)");
            });

        modelBuilder.Entity<Member>()
            .HasIndex(m => m.PhoneNumber)
            .IsUnique();

        modelBuilder.Entity<Member>()
            .HasIndex(m => m.Email)
            .IsUnique();

        modelBuilder.Entity<Member>()
            .HasIndex(m => m.QRCode)
            .IsUnique();

        modelBuilder.Entity<MembershipPlan>()
            .HasIndex(p => p.PlanName)
            .IsUnique();

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.ReceiptNumber)
            .IsUnique();

        // Explicit decimal precision for SQL Server
        modelBuilder.Entity<MembershipPlan>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Discount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Payment>()
            .Property(p => p.FinalAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<WalkInVisitor>()
            .Property(v => v.FeePaid)
            .HasPrecision(18, 2);

        // Relational Integrity & Delete Behaviors
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Member)
            .WithMany()
            .HasForeignKey(p => p.MemberID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Subscription)
            .WithMany()
            .HasForeignKey(p => p.SubscriptionID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasOne(u => u.Member)
            .WithOne()
            .HasForeignKey<User>(u => u.MemberID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AccountInvite>()
            .Property(i => i.TokenHash)
            .HasColumnType("binary(32)")
            .HasMaxLength(32)
            .IsFixedLength();

        modelBuilder.Entity<AccountInvite>()
            .Property(i => i.NormalizedEmail)
            .UseCollation("Latin1_General_100_BIN2");

        modelBuilder.Entity<AccountInvite>()
            .Property(i => i.UsedByFirebaseUid)
            .UseCollation("Latin1_General_100_BIN2");

        modelBuilder.Entity<AccountInvite>()
            .HasIndex(i => i.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_AccountInvites_TokenHash");

        modelBuilder.Entity<AccountInvite>()
            .HasIndex(i => i.NormalizedEmail)
            .HasDatabaseName("IX_AccountInvites_NormalizedEmail");

        modelBuilder.Entity<AccountInvite>()
            .HasIndex(i => i.RedemptionOperationId)
            .IsUnique()
            .HasDatabaseName("UX_AccountInvites_RedemptionOperationId")
            .HasFilter("[RedemptionOperationId] IS NOT NULL");

        modelBuilder.Entity<AccountInvite>()
            .ToTable("AccountInvites", table =>
            {
                table.HasCheckConstraint(
                    "CK_AccountInvites_ExactlyOneTarget",
                    "([TargetMemberID] IS NOT NULL AND [TargetUserID] IS NULL) OR ([TargetMemberID] IS NULL AND [TargetUserID] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_AccountInvites_ExpiryAfterCreation",
                    "[ExpiresAtUtc] > [CreatedAtUtc]");
                table.HasCheckConstraint(
                    "CK_AccountInvites_NormalizedEmailNotBlank",
                    "LEN([NormalizedEmail]) > 0");
                table.HasCheckConstraint(
                    "CK_AccountInvites_PurposeNotBlank",
                    "LEN(LTRIM(RTRIM([Purpose]))) > 0");
                table.HasCheckConstraint(
                    "CK_AccountInvites_RevokedTimestampAfterCreation",
                    "[RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]");
                table.HasCheckConstraint(
                    "CK_AccountInvites_TargetRole",
                    "([TargetMemberID] IS NOT NULL AND [IntendedRole] = 2) OR ([TargetUserID] IS NOT NULL AND [IntendedRole] IN (0, 1))");
                table.HasCheckConstraint(
                    "CK_AccountInvites_RedemptionMetadata",
                    "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR ([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL AND [RedemptionOperationId] IS NOT NULL AND [RedemptionOperationId] <> CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))");
                table.HasCheckConstraint(
                    "CK_AccountInvites_UsedOrRevoked",
                    "[UsedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL");
                table.HasCheckConstraint(
                    "CK_AccountInvites_UsedBeforeExpiry",
                    "[UsedAtUtc] IS NULL OR [UsedAtUtc] < [ExpiresAtUtc]");
                table.HasCheckConstraint(
                    "CK_AccountInvites_UsedTimestampAfterCreation",
                    "[UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]");
                table.HasCheckConstraint(
                    "CK_AccountInvites_UsedUidNotBlank",
                    "[UsedByFirebaseUid] IS NULL OR LEN([UsedByFirebaseUid]) > 0");
            });

        modelBuilder.Entity<AccountInvite>()
            .HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AccountInvite>()
            .HasOne(i => i.TargetMember)
            .WithMany()
            .HasForeignKey(i => i.TargetMemberID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AccountInvite>()
            .HasOne(i => i.TargetUser)
            .WithMany()
            .HasForeignKey(i => i.TargetUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MemberProjectionVersion>()
            .Property(v => v.Version)
            .HasDefaultValue(0L);

        modelBuilder.Entity<MemberProjectionVersion>()
            .ToTable("MemberProjectionVersions", table => table.HasCheckConstraint(
                "CK_MemberProjectionVersions_VersionRange",
                $"[Version] >= 0 AND [Version] <= {MemberProjectionVersion.MaximumVersion}"));

        modelBuilder.Entity<MemberProjectionVersion>()
            .HasOne(v => v.Member)
            .WithOne()
            .HasForeignKey<MemberProjectionVersion>(v => v.MemberID)
            .OnDelete(DeleteBehavior.Cascade);

        // Preserve the original datetime2 value while the capstone attendance model
        // moves to a local calendar date. New rows do not need a legacy value.
        modelBuilder.Entity<Attendance>()
            .Property<DateTime?>("AttendanceDateLegacyDateTime")
            .HasColumnType("datetime2");

        modelBuilder.Entity<Attendance>()
            .Property(a => a.AttendanceDate)
            .HasColumnType("date");

        modelBuilder.Entity<Attendance>()
            .HasIndex(a => new { a.MemberID, a.AttendanceDate })
            .IsUnique()
            .HasDatabaseName("UX_AttendanceLogs_Member_AttendanceDate_NonVoided")
            .HasFilter("[IsVoided] = 0");

        modelBuilder.Entity<Attendance>()
            .HasIndex(a => a.MemberID)
            .IsUnique()
            .HasDatabaseName("UX_AttendanceLogs_Member_Open_NonVoided")
            .HasFilter("[CheckOutTime] IS NULL AND [IsVoided] = 0");

        modelBuilder.Entity<Attendance>()
            .HasIndex(a => a.AttendanceDate)
            .HasDatabaseName("IX_AttendanceLogs_AttendanceDate");

        modelBuilder.Entity<Attendance>()
            .HasIndex(a => a.CheckInTime)
            .HasDatabaseName("IX_AttendanceLogs_CheckInTime");

        modelBuilder.Entity<Attendance>()
            .HasIndex(a => new { a.MemberID, a.CheckInTime })
            .HasDatabaseName("IX_AttendanceLogs_MemberID_CheckInTime");

        modelBuilder.Entity<Attendance>()
            .ToTable("AttendanceLogs", table =>
            {
                table.HasCheckConstraint(
                    "CK_AttendanceLogs_CheckoutAfterCheckin",
                    "[IsVoided] = 1 OR [CheckOutTime] IS NULL OR [CheckOutTime] > [CheckInTime]");
                table.HasCheckConstraint(
                    "CK_AttendanceLogs_VoidMetadata",
                    "([IsVoided] = 0 AND [VoidActorUserID] IS NULL AND [VoidedAtUtc] IS NULL AND [VoidReason] IS NULL) OR ([IsVoided] = 1 AND [VoidActorUserID] IS NOT NULL AND [VoidedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([VoidReason]))) > 0)");
                table.HasCheckConstraint(
                    "CK_AttendanceLogs_NoSelfSupersession",
                    "[SupersededByAttendanceID] IS NULL OR [SupersededByAttendanceID] <> [AttendanceID]");
                table.HasCheckConstraint(
                    "CK_AttendanceLogs_SupersessionRequiresVoid",
                    "[SupersededByAttendanceID] IS NULL OR [IsVoided] = 1");
            });

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.Member)
            .WithMany()
            .HasForeignKey(a => a.MemberID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.VoidActorUser)
            .WithMany()
            .HasForeignKey(a => a.VoidActorUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Attendance>()
            .HasOne(a => a.SupersededByAttendance)
            .WithMany()
            .HasForeignKey(a => a.SupersededByAttendanceID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceOperation>()
            .HasOne(o => o.ActorUser)
            .WithMany()
            .HasForeignKey(o => o.ActorUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceOperation>()
            .HasOne(o => o.TargetAttendance)
            .WithMany(a => a.Operations)
            .HasForeignKey(o => o.TargetAttendanceID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceAdjustment>()
            .HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceAdjustment>()
            .HasOne(a => a.Attendance)
            .WithMany(a => a.Adjustments)
            .HasForeignKey(a => a.AttendanceID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceAdjustment>()
            .HasOne(a => a.Operation)
            .WithOne(o => o.Adjustment)
            .HasForeignKey<AttendanceAdjustment>(a => a.OperationID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AttendanceAdjustment>()
            .ToTable("AttendanceAdjustments", table =>
            {
                table.HasCheckConstraint(
                    "CK_AttendanceAdjustments_Kind",
                    "[Kind] IN (0, 1, 2)");
                table.HasCheckConstraint(
                    "CK_AttendanceAdjustments_ReasonNotBlank",
                    "LEN(LTRIM(RTRIM([Reason]))) > 0");
            });

        modelBuilder.Entity<AttendanceOperation>()
            .Property(o => o.RequestFingerprint)
            .HasColumnType("binary(32)")
            .HasMaxLength(32)
            .IsFixedLength();

        modelBuilder.Entity<AttendanceOperation>()
            .ToTable("AttendanceOperations", table =>
            {
                table.HasCheckConstraint(
                    "CK_AttendanceOperations_OperationType",
                    "[OperationType] IN (0, 1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "CK_AttendanceOperations_State",
                    "[State] IN (0, 1)");
                table.HasCheckConstraint(
                    "CK_AttendanceOperations_ResultCodeNotBlank",
                    "LEN(LTRIM(RTRIM([OriginalResultCode]))) > 0");
                table.HasCheckConstraint(
                    "CK_AttendanceOperations_HttpStatusRange",
                    "[OriginalHttpStatus] BETWEEN 100 AND 599");
                table.HasCheckConstraint(
                    "CK_AttendanceOperations_CompletionOrder",
                    "[CompletedAtUtc] >= [CreatedAtUtc]");
            });

        // MemberApplication Entity Relationships & Constraints
        modelBuilder.Entity<MemberApplication>()
            .HasOne(a => a.SelectedPlan)
            .WithMany()
            .HasForeignKey(a => a.SelectedPlanID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MemberApplication>()
            .HasOne(a => a.VerifiedByUser)
            .WithMany()
            .HasForeignKey(a => a.VerifiedByUserID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MemberApplication>()
            .ToTable("MemberApplications", table =>
            {
                table.HasCheckConstraint(
                    "CK_MemberApplications_ExactlyOnePlanOrPass",
                    "([IsOneDayPass] = 1 AND [SelectedPlanID] IS NULL) OR ([IsOneDayPass] = 0 AND [SelectedPlanID] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_MemberApplications_Status",
                    "[ApplicationStatus] IN (0, 1, 2)");
                table.HasCheckConstraint(
                    "CK_MemberApplications_PaymentStatus",
                    "[PaymentStatus] IN (0, 1, 2, 3, 4, 5, 6, 7, 8)");
                table.HasCheckConstraint(
                    "CK_MemberApplications_PaymentMethod",
                    "[PaymentMethod] IN (0, 1, 2, 3, 4)");
            });

        // Seed Default System Settings
        var seedDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<SystemSetting>().HasData(
            new SystemSetting { SettingKey = "GymName", SettingValue = "GymTrackPro", GroupName = "General", Description = "Name of the gym facility.", LastModified = seedDate },
            new SystemSetting { SettingKey = "ContactNumber", SettingValue = "+639170000000", GroupName = "General", Description = "Gym contact helpline phone number.", LastModified = seedDate },
            new SystemSetting { SettingKey = "Currency", SettingValue = "PHP", GroupName = "General", Description = "Currency code used for financial billing transactions.", LastModified = seedDate },
            new SystemSetting { SettingKey = "Timezone", SettingValue = "Asia/Manila", GroupName = "General", Description = "System local timezone identifier.", LastModified = seedDate },
            new SystemSetting { SettingKey = "QRPrefix", SettingValue = "GTP-", GroupName = "Membership", Description = "Format prefix added to automatically generated member QR codes.", LastModified = seedDate },
            new SystemSetting { SettingKey = "ReceiptPrefix", SettingValue = "REC-", GroupName = "Payments", Description = "Format prefix added to payment invoice transaction receipts.", LastModified = seedDate },
            new SystemSetting { SettingKey = "MaxUploadSize", SettingValue = "5242880", GroupName = "Security", Description = "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", LastModified = seedDate },
            new SystemSetting { SettingKey = "AllowedImageTypes", SettingValue = ".jpg,.jpeg,.png", GroupName = "Security", Description = "Comma-separated list of approved image file extensions.", LastModified = seedDate },
            new SystemSetting { SettingKey = "PasswordPolicyRegex", SettingValue = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", GroupName = "Security", Description = "Regex pattern validating password strength rules.", LastModified = seedDate },
            new SystemSetting { SettingKey = "ReminderDaysBeforeExpiration", SettingValue = "3", GroupName = "Membership", Description = "Days ahead of membership expiration to raise alerts or send reminders.", LastModified = seedDate },
            new SystemSetting { SettingKey = "StaleSessionHours", SettingValue = "16", GroupName = "Attendance", Description = "Hours after which an open attendance session is considered stale.", LastModified = seedDate }
        );
    }
}
