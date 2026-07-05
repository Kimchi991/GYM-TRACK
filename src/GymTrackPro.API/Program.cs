using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using GymTrackPro.API.Data;
using GymTrackPro.API.Middleware;
using GymTrackPro.API.Repositories;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Events.Members;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Events.Authentication;
using GymTrackPro.Shared.Events.Attendance;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddHttpContextAccessor();

// Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("Global", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("Auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// Configure EF Core with SQL Server
builder.Services.AddDbContext<GymDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://securetoken.google.com/fithub-cf45f";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"\n[FIREBASE AUTH ERROR]: {context.Exception.Message}\n");
                return Task.CompletedTask;
            }
        };
    });

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMemberRepository, MemberRepository>();
builder.Services.AddScoped<IMembershipPlanRepository, MembershipPlanRepository>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<IWalkInVisitorRepository, WalkInVisitorRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();

// Register Services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddScoped<IMembershipPlanService, MembershipPlanService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IFirebaseNotificationService, FirebaseNotificationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportsService, ReportsService>();
builder.Services.AddScoped<ISystemSettingService, SystemSettingService>();

// Register Domain Event Dispatching System
builder.Services.AddScoped<IDomainEventPublisher, InMemoryEventPublisher>();
builder.Services.AddScoped<IDomainEventHandler<MemberRegisteredEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<PaymentReceivedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<RefundProcessedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipPausedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipResumedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<PasswordResetRequestedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipExpiringEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<CheckInFailedEvent>, NotificationHandler>();

// Register Notification Queue and Background Worker
builder.Services.AddSingleton<INotificationQueue, MemoryNotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();

// Register Hosted Services
builder.Services.AddHostedService<SubscriptionExpirationWorker>();


// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Only redirect to HTTPS in production. Doing this locally causes 
    // HttpClient to strip the Authorization header on 307 redirects!
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

// Seed settings if table is empty
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GymDbContext>();
    if (!context.SystemSettings.Any())
    {
        var seedDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
        context.SystemSettings.AddRange(
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "GymName", SettingValue = "GymTrackPro", GroupName = "General", Description = "Name of the gym facility.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "ContactNumber", SettingValue = "+639170000000", GroupName = "General", Description = "Gym contact helpline phone number.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "Currency", SettingValue = "PHP", GroupName = "General", Description = "Currency code used for financial billing transactions.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "Timezone", SettingValue = "Asia/Manila", GroupName = "General", Description = "System local timezone identifier.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "QRPrefix", SettingValue = "GTP-", GroupName = "Membership", Description = "Format prefix added to automatically generated member QR codes.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "ReceiptPrefix", SettingValue = "REC-", GroupName = "Payments", Description = "Format prefix added to payment invoice transaction receipts.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "MaxUploadSize", SettingValue = "5242880", GroupName = "Security", Description = "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "AllowedImageTypes", SettingValue = ".jpg,.jpeg,.png", GroupName = "Security", Description = "Comma-separated list of approved image file extensions.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "PasswordPolicyRegex", SettingValue = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", GroupName = "Security", Description = "Regex pattern validating password strength rules.", LastModified = seedDate },
            new GymTrackPro.Shared.Entities.SystemSetting { SettingKey = "ReminderDaysBeforeExpiration", SettingValue = "3", GroupName = "Membership", Description = "Days ahead of membership expiration to raise alerts or send reminders.", LastModified = seedDate }
        );
        context.SaveChanges();
    }
}

app.Run();
