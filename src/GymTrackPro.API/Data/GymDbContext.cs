using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Data;

public class GymDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public GymDbContext(DbContextOptions<GymDbContext> options, ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public int? CurrentGymID => _tenantProvider.GetTenantId();
    public bool IsPlatformAdmin => _tenantProvider.IsPlatformAdmin();

    public DbSet<Gym> Gyms => Set<Gym>();
    public DbSet<GymSubscription> GymSubscriptions => Set<GymSubscription>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<GymSetting> GymSettings => Set<GymSetting>();
    public DbSet<GymInvitation> GymInvitations => Set<GymInvitation>();

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

    public override Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        ApplyTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTenantId();
        return base.SaveChanges();
    }

    private void ApplyTenantId()
    {
        var tenantId = CurrentGymID ?? 1;

        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in entries)
        {
            if (entry.Entity.GetType() == typeof(Gym))
            {
                continue;
            }

            var gymIdProp = entry.Entity.GetType().GetProperty("GymID");
            if (gymIdProp != null && gymIdProp.CanWrite)
            {
                var currentValue = gymIdProp.GetValue(entry.Entity);
                if (currentValue == null || (currentValue is int val && val == 0))
                {
                    gymIdProp.SetValue(entry.Entity, tenantId);
                }
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Composite Key for GymSetting
        modelBuilder.Entity<GymSetting>()
            .HasKey(gs => new { gs.GymID, gs.SettingKey });

        // Global query filters
        modelBuilder.Entity<Gym>().HasQueryFilter(g => IsPlatformAdmin || g.GymID == CurrentGymID);
        modelBuilder.Entity<User>().HasQueryFilter(u => IsPlatformAdmin || u.GymID == CurrentGymID);
        modelBuilder.Entity<Member>().HasQueryFilter(m => m.GymID == CurrentGymID && !m.IsDeleted);
        modelBuilder.Entity<MembershipPlan>().HasQueryFilter(mp => mp.GymID == CurrentGymID && !mp.IsDeleted);
        modelBuilder.Entity<Subscription>().HasQueryFilter(s => s.GymID == CurrentGymID);
        modelBuilder.Entity<MembershipPause>().HasQueryFilter(mp => mp.GymID == CurrentGymID);
        modelBuilder.Entity<Payment>().HasQueryFilter(p => p.GymID == CurrentGymID && !p.IsDeleted);
        modelBuilder.Entity<Attendance>().HasQueryFilter(a => a.GymID == CurrentGymID);
        modelBuilder.Entity<WalkInVisitor>().HasQueryFilter(w => w.GymID == CurrentGymID);
        modelBuilder.Entity<Notification>().HasQueryFilter(n => n.GymID == CurrentGymID);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => IsPlatformAdmin || a.GymID == CurrentGymID);
        modelBuilder.Entity<GymSetting>().HasQueryFilter(gs => gs.GymID == CurrentGymID);
        modelBuilder.Entity<GymInvitation>().HasQueryFilter(gi => gi.GymID == CurrentGymID);

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

        modelBuilder.Entity<SubscriptionPlan>()
            .Property(sp => sp.Price)
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

        // Avoid multiple cascade paths by using Restrict/NoAction for Gym-scoped entities
        foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            if (relationship.DeleteBehavior == DeleteBehavior.Cascade && relationship.PrincipalEntityType.ClrType == typeof(Gym))
            {
                relationship.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        // Seed data dates
        var seedDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);

        // Seed Default Gym Tenant (ID = 1)
        modelBuilder.Entity<Gym>().HasData(
            new Gym
            {
                GymID = 1,
                Name = "Default Gym",
                Address = "Main Street",
                ContactNumber = "+639170000000",
                Capacity = 500,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            }
        );

        // Seed Default Subscription Plans
        modelBuilder.Entity<SubscriptionPlan>().HasData(
            new SubscriptionPlan { PlanID = 1, Name = "Standard", Price = 99.00m, MaxMembers = 500, Description = "Standard multi-tenant plan.", BillingCycleMonths = 1 },
            new SubscriptionPlan { PlanID = 2, Name = "Premium", Price = 199.00m, MaxMembers = 2000, Description = "Premium multi-tenant plan.", BillingCycleMonths = 1 }
        );

        // Seed Default Gym Subscription Link
        modelBuilder.Entity<GymSubscription>().HasData(
            new GymSubscription
            {
                SubscriptionID = 1,
                GymID = 1,
                PlanID = 1,
                Status = GymTrackPro.Shared.Enums.SubscriptionStatus.Active,
                StartedAt = seedDate,
                ExpiresAt = seedDate.AddYears(10),
                TrialEndsAt = seedDate.AddDays(14)
            }
        );

        // Seed Default Gym Settings (GymID = 1)
        modelBuilder.Entity<GymSetting>().HasData(
            new GymSetting { GymID = 1, SettingKey = "GymName", SettingValue = "GymTrackPro", GroupName = "General", Description = "Name of the gym facility.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "ContactNumber", SettingValue = "+639170000000", GroupName = "General", Description = "Gym contact helpline phone number.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "Currency", SettingValue = "PHP", GroupName = "General", Description = "Currency code used for financial billing transactions.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "Timezone", SettingValue = "Asia/Manila", GroupName = "General", Description = "System local timezone identifier.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "QRPrefix", SettingValue = "GTP-", GroupName = "Membership", Description = "Format prefix added to automatically generated member QR codes.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "ReceiptPrefix", SettingValue = "REC-", GroupName = "Payments", Description = "Format prefix added to payment invoice transaction receipts.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "MaxUploadSize", SettingValue = "5242880", GroupName = "Security", Description = "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "AllowedImageTypes", SettingValue = ".jpg,.jpeg,.png", GroupName = "Security", Description = "Comma-separated list of approved image file extensions.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "PasswordPolicyRegex", SettingValue = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", GroupName = "Security", Description = "Regex pattern validating password strength rules.", LastModified = seedDate },
            new GymSetting { GymID = 1, SettingKey = "ReminderDaysBeforeExpiration", SettingValue = "3", GroupName = "Membership", Description = "Days ahead of membership expiration to raise alerts or send reminders.", LastModified = seedDate }
        );
    }
}
