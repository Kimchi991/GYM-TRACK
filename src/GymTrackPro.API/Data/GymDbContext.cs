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
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<MembershipPause> MembershipPauses => Set<MembershipPause>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Attendance> AttendanceLogs => Set<Attendance>();
    public DbSet<WalkInVisitor> WalkInVisitors => Set<WalkInVisitor>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

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
            new SystemSetting { SettingKey = "ReminderDaysBeforeExpiration", SettingValue = "3", GroupName = "Membership", Description = "Days ahead of membership expiration to raise alerts or send reminders.", LastModified = seedDate }
        );
    }
}
