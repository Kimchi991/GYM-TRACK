using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class ApplicationService : IApplicationService
{
    private readonly GymDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IClockService _clock;
    private readonly ISystemSettingService _settingsService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ApplicationService(
        GymDbContext context,
        IEmailService emailService,
        IClockService clock,
        ISystemSettingService settingsService,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _emailService = emailService;
        _clock = clock;
        _settingsService = settingsService;
        _httpContextAccessor = httpContextAccessor;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<ApplicationListItemDto> SubmitApplicationAsync(SubmitApplicationDto dto)
    {
        if (dto is null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var duplicatePending = await _context.MemberApplications.AnyAsync(a =>
            a.ApplicationStatus == ApplicationStatus.Pending &&
            (a.EmailAddress == dto.EmailAddress || a.ContactNumber == dto.ContactNumber));
        if (duplicatePending)
        {
            throw new ArgumentException("An application with the same email address or contact number is already pending verification.");
        }

        // Validate plan if not a one-day pass
        if (!dto.IsOneDayPass)
        {
            if (!dto.SelectedPlanID.HasValue)
            {
                throw new ArgumentException("A membership plan must be selected for standard memberships.");
            }
            var planExists = await _context.MembershipPlans.AnyAsync(p => p.PlanID == dto.SelectedPlanID.Value && p.Status == "Active");
            if (!planExists)
            {
                throw new KeyNotFoundException("The selected membership plan is not available.");
            }
        }
        else
        {
            if (dto.SelectedPlanID.HasValue)
            {
                throw new ArgumentException("A membership plan cannot be selected for a one-day pass.");
            }
        }

        var application = new MemberApplication
        {
            FullName = dto.FullName,
            ContactNumber = dto.ContactNumber,
            EmailAddress = dto.EmailAddress,
            EmergencyContact = dto.EmergencyContact,
            SelectedPlanID = dto.SelectedPlanID,
            IsOneDayPass = dto.IsOneDayPass,
            PaymentMethod = dto.PaymentMethod,
            PaymentReferenceNumber = dto.PaymentReferenceNumber,
            PaymentStatus = PaymentStatus.Pending,
            ApplicationStatus = ApplicationStatus.Pending,
            CreatedAtUtc = _clock.UtcNow
        };

        _context.MemberApplications.Add(application);
        await _context.SaveChangesAsync();

        return await MapToDtoAsync(application);
    }

    public async Task<IEnumerable<ApplicationListItemDto>> GetPendingApplicationsAsync()
    {
        var list = await _context.MemberApplications
            .Include(a => a.SelectedPlan)
            .Include(a => a.VerifiedByUser)
            .Where(a => a.ApplicationStatus == ApplicationStatus.Pending)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync();

        var dtos = new List<ApplicationListItemDto>();
        foreach (var item in list)
        {
            dtos.Add(await MapToDtoAsync(item));
        }
        return dtos;
    }

    public async Task<ApplicationListItemDto> VerifyApplicationAsync(int id, int actorUserId, VerifyApplicationDto dto)
    {
        if (dto is null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var application = await _context.MemberApplications
            .Include(a => a.SelectedPlan)
            .SingleOrDefaultAsync(a => a.ApplicationID == id);

        if (application is null)
        {
            throw new KeyNotFoundException("The application was not found.");
        }

        if (application.ApplicationStatus != ApplicationStatus.Pending)
        {
            throw new InvalidOperationException("This application has already been processed.");
        }

        var verifier = await _context.Users.FindAsync(actorUserId);
        if (verifier is null)
        {
            throw new KeyNotFoundException("The verifier staff user account was not found.");
        }

        var now = _clock.UtcNow;

        if (dto.Status == ApplicationStatus.Rejected)
        {
            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
            {
                throw new ArgumentException("A rejection reason must be supplied.");
            }

            application.ApplicationStatus = ApplicationStatus.Rejected;
            application.PaymentStatus = PaymentStatus.Failed;
            application.RejectionReason = dto.RejectionReason;
            application.VerifiedAtUtc = now;
            application.VerifiedByUserID = actorUserId;

            await _context.SaveChangesAsync();

            // Log Audit activity
            _context.AuditLogs.Add(new AuditLog
            {
                UserID = actorUserId,
                Action = "MemberApplicationRejected",
                Details = $"Registration application from {application.FullName} (Email: {application.EmailAddress}) rejected: {dto.RejectionReason}.",
                IPAddress = GetClientIpAddress(),
                Timestamp = now
            });
            await _context.SaveChangesAsync();

            return await MapToDtoAsync(application);
        }

        // Process Approval
        application.ApplicationStatus = ApplicationStatus.Approved;
        application.PaymentStatus = PaymentStatus.Paid;
        application.VerifiedAtUtc = now;
        application.VerifiedByUserID = actorUserId;

        if (application.IsOneDayPass)
        {
            var tempQrCode = "GTP-WALKIN-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            // Walk-in pass: Log visitor access instantly with a 24-hour scannable gate QR ticket
            var walkIn = new WalkInVisitor
            {
                VisitorName = application.FullName,
                VisitDate = now.Date,
                FeePaid = 100.00m, // Authoritative one-day pass fee
                Purpose = "Self-Service Walk-In Pass Approved",
                TemporaryQRCode = tempQrCode,
                ExpiresAtUtc = now.AddHours(24)
            };
            _context.WalkInVisitors.Add(walkIn);

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = actorUserId,
                Action = "WalkInPassApproved",
                Details = $"Walk-in one-day pass approved for {application.FullName}.",
                IPAddress = GetClientIpAddress(),
                Timestamp = now
            });
        }
        else
        {
            // Standard Membership Onboarding flow
            var (firstName, lastName) = ParseFullName(application.FullName);

            // 1. Generate unique QR Code
            var qrPrefix = await _settingsService.GetValueAsync("QRPrefix", "GTP-");
            string qrCode;
            while (true)
            {
                qrCode = qrPrefix + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();
                var existingQR = await _context.Members.AnyAsync(m => m.QRCode == qrCode);
                if (!existingQR) break;
            }

            // 2. Create Member
            var member = new Member
            {
                FirstName = firstName,
                LastName = lastName,
                Email = application.EmailAddress,
                PhoneNumber = application.ContactNumber,
                EmergencyContact = application.EmergencyContact ?? application.ContactNumber,
                Gender = "Other", // Default value for registration forms
                BirthDate = now.AddYears(-20), // Placeholder default registration age
                QRCode = qrCode,
                Status = "Active",
                DateRegistered = now,
                LastModified = now,
                IsDeleted = false
            };
            _context.Members.Add(member);
            await _context.SaveChangesAsync(); // Materialize MemberID

            // 3. Create Subscription
            var plan = application.SelectedPlan ?? await _context.MembershipPlans.FindAsync(application.SelectedPlanID);
            if (plan is null)
            {
                throw new KeyNotFoundException("The plan selected by the applicant is unavailable.");
            }

            var subscription = new Subscription
            {
                MemberID = member.MemberID,
                PlanID = plan.PlanID,
                StartDate = now.Date,
                EndDate = now.Date.AddDays(plan.DurationDays),
                Status = "Active",
                LastModified = now
            };
            _context.Subscriptions.Add(subscription);
            await _context.SaveChangesAsync(); // Materialize SubscriptionID

            // 4. Create Payment
            var receiptPrefix = await _settingsService.GetValueAsync("ReceiptPrefix", "REC-");
            var entropy = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
            var receiptNumber = $"{receiptPrefix}{now:yyMMddHHmmss}-{entropy}";

            var payment = new Payment
            {
                MemberID = member.MemberID,
                SubscriptionID = subscription.SubscriptionID,
                Amount = plan.Price,
                Discount = 0.00m,
                FinalAmount = plan.Price,
                PaymentMethod = application.PaymentMethod,
                PaymentStatus = PaymentStatus.Paid,
                ReceiptNumber = receiptNumber,
                ReferenceNumber = application.PaymentReferenceNumber,
                DatePaid = now,
                LastModified = now
            };
            _context.Payments.Add(payment);

            // 5. Create Projection Version
            var projectionVersion = new MemberProjectionVersion
            {
                MemberID = member.MemberID,
                Version = 0
            };
            _context.MemberProjectionVersions.Add(projectionVersion);

            // 6. Create Account Invite (GymGoer role activation token)
            var inviteCode = InviteCodeCodec.Generate();
            _ = InviteCodeCodec.TryHash(inviteCode, out var tokenHash);

            var invite = new AccountInvite
            {
                TargetMemberID = member.MemberID,
                TokenHash = tokenHash.ToArray(),
                NormalizedEmail = application.EmailAddress.ToUpperInvariant(),
                IntendedRole = UserRole.GymGoer,
                Purpose = "Automated onboarding invite for approved registration",
                CreatedByUserID = actorUserId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(72)
            };
            _context.AccountInvites.Add(invite);

            _context.AuditLogs.Add(new AuditLog
            {
                UserID = actorUserId,
                Action = "MemberApplicationApproved",
                Details = $"Registration application approved. Created Member ID: {member.MemberID}; Subscription ID: {subscription.SubscriptionID}; Invite code generated.",
                IPAddress = GetClientIpAddress(),
                Timestamp = now
            });

            // 7. Trigger Email
            try
            {
                var body = $"Hello {application.FullName},\n\n" +
                           $"Your registration application has been verified and approved!\n\n" +
                           $"Please download the GymTrackPro app and enter your Activation Code during registration to link your membership:\n" +
                           $"Activation Code: {inviteCode}\n\n" +
                           $"Welcome to our gym community!";
                await _emailService.SendEmailAsync(application.EmailAddress, "Welcome to GymTrackPro! Account Activation", body);
            }
            catch
            {
                // Soft fail email delivery; we must still commit the database registration changes
            }
        }

        await _context.SaveChangesAsync();
        return await MapToDtoAsync(application);
    }

    private static (string FirstName, string LastName) ParseFullName(string fullName)
    {
        var trimmed = fullName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ("Walk-in", "Guest");
        }
        var idx = trimmed.IndexOf(' ');
        if (idx < 0)
        {
            return (trimmed, "Guest");
        }
        return (trimmed.Substring(0, idx).Trim(), trimmed.Substring(idx + 1).Trim());
    }

    private async Task<ApplicationListItemDto> MapToDtoAsync(MemberApplication app)
    {
        var planName = "One-Day Pass";
        decimal price = 100.00m; // Default one-day pass fee

        if (app.SelectedPlanID.HasValue)
        {
            var plan = app.SelectedPlan ?? await _context.MembershipPlans.FindAsync(app.SelectedPlanID.Value);
            if (plan is not null)
            {
                planName = plan.PlanName;
                price = plan.Price;
            }
        }

        var verifierUsername = string.Empty;
        if (app.VerifiedByUserID.HasValue)
        {
            var user = app.VerifiedByUser ?? await _context.Users.FindAsync(app.VerifiedByUserID.Value);
            if (user is not null)
            {
                verifierUsername = user.Username;
            }
        }

        var tempQr = (string?)null;
        if (app.IsOneDayPass && app.ApplicationStatus == ApplicationStatus.Approved)
        {
            tempQr = await _context.WalkInVisitors
                .Where(w => w.VisitorName == app.FullName && w.TemporaryQRCode != null)
                .OrderByDescending(w => w.VisitDate)
                .Select(w => w.TemporaryQRCode)
                .FirstOrDefaultAsync();
        }

        return new ApplicationListItemDto
        {
            ApplicationID = app.ApplicationID,
            FullName = app.FullName,
            ContactNumber = app.ContactNumber,
            EmailAddress = app.EmailAddress,
            EmergencyContact = app.EmergencyContact,
            SelectedPlanID = app.SelectedPlanID,
            SelectedPlanName = planName,
            Price = price,
            IsOneDayPass = app.IsOneDayPass,
            PaymentMethod = app.PaymentMethod,
            PaymentReferenceNumber = app.PaymentReferenceNumber,
            PaymentStatus = app.PaymentStatus,
            ApplicationStatus = app.ApplicationStatus,
            CreatedAtUtc = app.CreatedAtUtc,
            VerifiedAtUtc = app.VerifiedAtUtc,
            VerifiedByUsername = verifierUsername,
            RejectionReason = app.RejectionReason,
            TemporaryQRCode = tempQr
        };
    }
}
