using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using GymTrackPro.API.Data;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Security;
using Microsoft.AspNetCore.Authorization;
using GymTrackPro.API.Middleware;
using GymTrackPro.API.Repositories;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Events.Members;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Events.Attendance;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddHttpContextAccessor();

var isDevelopmentOrTesting = builder.Environment.IsDevelopment()
    || builder.Environment.IsEnvironment("Testing");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection must be supplied through environment or local secret configuration.");
}

var allowedHosts = builder.Configuration["AllowedHosts"];
if (!isDevelopmentOrTesting && !ProductionAllowedHosts.IsValid(allowedHosts))
{
    throw new InvalidOperationException(
        "Production AllowedHosts must contain only explicit, non-loopback API hosts without wildcards, schemes, ports, or paths.");
}

builder.Services
    .AddOptions<FirebaseAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(FirebaseAuthenticationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FirebaseAuthenticationOptions>, FirebaseAuthenticationOptionsValidator>();

builder.Services
    .AddOptions<TrustedProxyOptions>()
    .Bind(builder.Configuration.GetSection(TrustedProxyOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();

builder.Services
    .AddOptions<PreAuthenticationRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(PreAuthenticationRateLimitOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<PreAuthenticationRateLimitOptions>,
    PreAuthenticationRateLimitOptionsValidator>();

var trustedProxySettings = builder.Configuration
    .GetSection(TrustedProxyOptions.SectionName)
    .Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = trustedProxySettings.ForwardLimit;
    options.RequireHeaderSymmetry = true;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var configuredProxy in trustedProxySettings.KnownProxies)
    {
        if (IPAddress.TryParse(configuredProxy, out var proxyAddress))
        {
            options.KnownProxies.Add(proxyAddress);
        }
    }
});

// Configure Rate Limiting
builder.Services.AddSingleton<ActivationInviteHashShardLimiter>();
builder.Services.AddSingleton<PreAuthenticationIpLimiter>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = RequestRateLimitRejectionHandler.HandleAsync;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RequestRateLimitPartition.Create(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("Auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RequestRateLimitPartition.Create(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("Activation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RequestRateLimitPartition.Create(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

// Configure EF Core with SQL Server
builder.Services.AddDbContext<GymDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

var firebaseSettings = builder.Configuration
    .GetSection(FirebaseAuthenticationOptions.SectionName)
    .Get<FirebaseAuthenticationOptions>() ?? new FirebaseAuthenticationOptions();

// Firebase proves identity only. SQL-derived claims are added by the claims transformation.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        FirebaseJwtConfiguration.Configure(options, firebaseSettings, builder.Environment);
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var env = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var remoteIp = context.HttpContext.Connection.RemoteIpAddress;
                var isLoopback = remoteIp == null || System.Net.IPAddress.IsLoopback(remoteIp);

                if (isLoopback &&
                    (env.IsDevelopment() || env.IsEnvironment("Testing")) &&
                    context.HttpContext.Request.Headers.TryGetValue("X-Test-User-Uid", out var uid) &&
                    context.HttpContext.Request.Headers.TryGetValue("X-Test-User-Email", out var email))
                {
                    var claims = new System.Collections.Generic.List<System.Security.Claims.Claim>
                    {
                        new System.Security.Claims.Claim("sub", uid.ToString()),
                        new System.Security.Claims.Claim("email", email.ToString()),
                        new System.Security.Claims.Claim("email_verified", "true"),
                        new System.Security.Claims.Claim("iat", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                        new System.Security.Claims.Claim("auth_time", System.DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
                    };
                    var identity = new System.Security.Claims.ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
                    context.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                    context.Success();
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = FirebaseJwtConfiguration.ValidateRequiredClaimsAsync,
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FirebaseAuthentication");
                logger.LogWarning(
                    "Firebase bearer authentication failed. CorrelationId: {CorrelationId}; ErrorType: {ErrorType}",
                    context.HttpContext.TraceIdentifier,
                    context.Exception.GetType().Name);
                return Task.CompletedTask;
            }
        };
    });

// Configure Authorization Policies
builder.Services.AddScoped<IUidAppUserResolver, UidAppUserResolver>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, FirebaseAppClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationHandler, AppAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    var activeAppUserPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveAppUserRequirement())
        .Build();

    // Bare [Authorize] and controllers without explicit metadata cannot authorize an
    // unknown Firebase identity. Onboarding explicitly opts into its narrower policy.
    options.DefaultPolicy = activeAppUserPolicy;
    options.FallbackPolicy = activeAppUserPolicy;

    options.AddPolicy(Policies.FirebaseOnboarding, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new FirebaseOnboardingRequirement());
    });

    options.AddPolicy(Policies.ActiveAppUser, activeAppUserPolicy);

    options.AddPolicy(Policies.BackOffice, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new BackOfficeRequirement());
    });

    options.AddPolicy(Policies.OwnerOnly, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new OwnerOnlyRequirement());
    });

    options.AddPolicy(Policies.GymGoerSelf, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new GymGoerSelfRequirement());
    });

    options.AddPolicy(Policies.TrainerOnly, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new TrainerOnlyRequirement());
    });
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
builder.Services.AddScoped<IAccountInviteRepository, AccountInviteRepository>();

// Register Services
builder.Services.AddScoped<IFirebaseEmailService, ConsoleFirebaseEmailService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IIdentityProvisioningStore, IdentityProvisioningStore>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddSingleton<IProfilePictureStorage, FileSystemProfilePictureStorage>();
builder.Services.AddScoped<IMemberDeletionTransaction, MemberDeletionTransaction>();
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
builder.Services.AddScoped<IClockService, ClockService>();
builder.Services.AddScoped<IGymGoerProjectionService, GymGoerProjectionService>();
builder.Services.AddScoped<ITimezoneService, TimezoneService>();
builder.Services.AddProjectionVersionInfrastructure();

// Register Domain Event Dispatching System
builder.Services.AddScoped<IDomainEventPublisher, InMemoryEventPublisher>();
builder.Services.AddScoped<IDomainEventHandler<MemberRegisteredEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<PaymentReceivedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<RefundProcessedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipPausedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipResumedEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<MembershipExpiringEvent>, NotificationHandler>();
builder.Services.AddScoped<IDomainEventHandler<CheckInFailedEvent>, NotificationHandler>();

// Register Notification Queue and Background Worker
builder.Services.AddSingleton<INotificationQueue, MemoryNotificationQueue>();
builder.Services.AddHostedService<NotificationWorker>();

// Register Hosted Services
builder.Services.AddHostedService<SubscriptionExpirationWorker>();
builder.Services.AddHostedService<OvernightSessionCleanupWorker>();


// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

var trustedProxyOptions = app.Services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
if (trustedProxyOptions.Enabled)
{
    app.UseForwardedHeaders();
}

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

// Explicit routing guarantees endpoint authorization metadata is available to the claims
// transformation before it decides whether an onboarding request requires SQL resolution.
app.UseRouting();

// Only the bounded per-IP admission control runs before bearer validation. The invite-code
// shard is charged only after Firebase authentication and the endpoint Activation limiter
// have admitted the request.
app.UseMiddleware<PreAuthenticationRateLimitMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<ActivationInviteRateLimitMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();
