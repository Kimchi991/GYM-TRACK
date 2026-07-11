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
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Events.Members;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Events.Authentication;
using GymTrackPro.Shared.Events.Attendance;

var builder = WebApplication.CreateBuilder(args);

// Configure Logging to avoid EventLog permission issues in development/non-admin hosts
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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
            ClockSkew = TimeSpan.FromMinutes(5),
            IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
            {
                var keys = new System.Collections.Generic.List<SecurityKey>();
                string? issuer = null;
                if (securityToken is System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtToken)
                {
                    issuer = jwtToken.Issuer;
                }
                else if (securityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jsonToken)
                {
                    issuer = jsonToken.Issuer;
                }

                if (issuer == (builder.Configuration["Jwt:Issuer"] ?? "GymTrackProAPI"))
                {
                    var keyBytes = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "GymTrackProSecretKeyPlaceholder123456");
                    keys.Add(new SymmetricSecurityKey(keyBytes));
                }
                return keys;
            }
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
builder.Services.AddScoped<TenantState>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
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
app.UseMiddleware<TenantResolverMiddleware>();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

app.Run();
