using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Events.Payments;

namespace GymTrackPro.API.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IMemberRepository _memberRepository;
    private readonly IMembershipPlanRepository _planRepository;
    private readonly GymDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ISystemSettingService _settingsService;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        IMemberRepository memberRepository,
        IMembershipPlanRepository planRepository,
        GymDbContext context,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        IDomainEventPublisher eventPublisher,
        IPaymentRepository paymentRepository,
        ISystemSettingService settingsService)
    {
        _subscriptionRepository = subscriptionRepository;
        _memberRepository = memberRepository;
        _planRepository = planRepository;
        _context = context;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _eventPublisher = eventPublisher;
        _paymentRepository = paymentRepository;
        _settingsService = settingsService;
    }

    private int? GetCurrentUserId()
    {
        var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int userId) ? userId : null;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<SubscriptionResponseDto?> GetByIdAsync(int id)
    {
        var sub = await _subscriptionRepository.GetByIdAsync(id);
        if (sub == null) return null;

        return MapToDto(sub);
    }

    public async Task<IEnumerable<SubscriptionResponseDto>> GetByMemberIdAsync(int memberId)
    {
        var subs = await _subscriptionRepository.GetByMemberIdAsync(memberId);
        return subs.Select(MapToDto);
    }

    public async Task<SubscriptionResponseDto> SubscribeMemberAsync(CreateSubscriptionDto subscribeDto)
    {
        var member = await _memberRepository.GetByIdAsync(subscribeDto.MemberID);
        if (member == null)
        {
            throw new KeyNotFoundException("Member not found.");
        }

        var plan = await _planRepository.GetByIdAsync(subscribeDto.PlanID);
        if (plan == null)
        {
            throw new KeyNotFoundException("Membership plan not found.");
        }

        // BR-01: Cannot activate membership unless payment succeeds.
        // Therefore, we register new subscriptions initially as "PendingPayment"
        var sub = new Subscription
        {
            MemberID = subscribeDto.MemberID,
            PlanID = subscribeDto.PlanID,
            StartDate = subscribeDto.StartDate,
            EndDate = subscribeDto.StartDate.AddDays(plan.DurationDays),
            Status = "PendingPayment",
            LastModified = DateTime.UtcNow
        };

        await _subscriptionRepository.AddAsync(sub);

        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "Subscription Initialized",
            $"Subscription initialized for member {member.FirstName} {member.LastName} (ID: {member.MemberID}) for plan '{plan.PlanName}'. Status: PendingPayment.",
            GetClientIpAddress()
        );

        // Load navigation properties for clean response mapping
        sub.Member = member;
        sub.Plan = plan;

        return MapToDto(sub);
    }

    public async Task PauseSubscriptionAsync(int subscriptionID, string reason)
    {
        var sub = await _subscriptionRepository.GetByIdAsync(subscriptionID);
        if (sub == null)
        {
            throw new KeyNotFoundException("Subscription not found.");
        }

        if (sub.Status != "Active")
        {
            throw new InvalidOperationException("Only active subscriptions can be paused.");
        }

        var pause = new MembershipPause
        {
            SubscriptionID = subscriptionID,
            PauseStartDate = DateTime.UtcNow,
            Reason = reason,
            DateCreated = DateTime.UtcNow
        };

        _context.MembershipPauses.Add(pause);
        sub.Status = "Paused";
        sub.LastModified = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "Subscription Paused",
            $"Subscription ID: {subscriptionID} paused. Reason: {reason}.",
            GetClientIpAddress()
        );

        // Publish Domain Event
        var member = sub.Member ?? await _context.Members.FindAsync(sub.MemberID);
        var plan = sub.Plan ?? await _context.MembershipPlans.FindAsync(sub.PlanID);
        await _eventPublisher.PublishAsync(new MembershipPausedEvent
        {
            SubscriptionId = sub.SubscriptionID,
            MemberId = sub.MemberID,
            MemberEmail = member?.Email ?? string.Empty,
            PlanName = plan?.PlanName ?? "Unknown"
        });
    }

    public async Task ResumeSubscriptionAsync(int subscriptionID)
    {
        var sub = await _subscriptionRepository.GetByIdAsync(subscriptionID);
        if (sub == null)
        {
            throw new KeyNotFoundException("Subscription not found.");
        }

        if (sub.Status != "Paused")
        {
            throw new InvalidOperationException("Subscription is not paused.");
        }

        var activePause = await _context.MembershipPauses
            .Where(p => p.SubscriptionID == subscriptionID && p.PauseEndDate == null)
            .OrderByDescending(p => p.PauseStartDate)
            .FirstOrDefaultAsync();

        if (activePause != null)
        {
            activePause.PauseEndDate = DateTime.UtcNow;
            
            // Calculate paused days to extend the EndDate
            var pausedDuration = DateTime.UtcNow - activePause.PauseStartDate;
            int pausedDays = (int)Math.Max(1, Math.Ceiling(pausedDuration.TotalDays));
            
            sub.EndDate = sub.EndDate.AddDays(pausedDays);
        }

        sub.Status = "Active";
        sub.LastModified = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "Subscription Resumed",
            $"Subscription ID: {subscriptionID} resumed. Validity extended.",
            GetClientIpAddress()
        );

        // Publish Domain Event
        var member = sub.Member ?? await _context.Members.FindAsync(sub.MemberID);
        var plan = sub.Plan ?? await _context.MembershipPlans.FindAsync(sub.PlanID);
        await _eventPublisher.PublishAsync(new MembershipResumedEvent
        {
            SubscriptionId = sub.SubscriptionID,
            MemberId = sub.MemberID,
            MemberEmail = member?.Email ?? string.Empty,
            PlanName = plan?.PlanName ?? "Unknown"
        });
    }

    public async Task<SubscriptionResponseDto> RenewSubscriptionAsync(RenewSubscriptionDto renewDto)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var member = await _memberRepository.GetByIdAsync(renewDto.MemberID);
            if (member == null)
            {
                throw new KeyNotFoundException("Member not found.");
            }

            var plan = await _planRepository.GetByIdAsync(renewDto.PlanID);
            if (plan == null)
            {
                throw new KeyNotFoundException("Membership plan not found.");
            }

            // Create Subscription - set status directly to Active as payment is processed atomically
            var sub = new Subscription
            {
                MemberID = renewDto.MemberID,
                PlanID = renewDto.PlanID,
                StartDate = renewDto.StartDate,
                EndDate = renewDto.StartDate.AddDays(plan.DurationDays),
                Status = "Active",
                LastModified = DateTime.UtcNow
            };

            await _subscriptionRepository.AddAsync(sub);

            if (!Enum.TryParse<PaymentMethod>(renewDto.PaymentMethod, true, out var method))
            {
                throw new ArgumentException($"Invalid payment method: {renewDto.PaymentMethod}");
            }

            if (method != PaymentMethod.Cash)
            {
                if (string.IsNullOrWhiteSpace(renewDto.ReferenceNumber))
                {
                    throw new ArgumentException("Reference number is required for online payments.");
                }

                var duplicateRef = await _context.Payments
                    .AnyAsync(p => p.ReferenceNumber == renewDto.ReferenceNumber && !p.IsDeleted);

                if (duplicateRef)
                {
                    throw new ArgumentException("A payment transaction with this reference number already exists.");
                }
            }

            var receiptPrefix = await _settingsService.GetValueAsync("ReceiptPrefix", "REC-");
            string receiptNumber = string.Empty;
            bool uniqueReceipt = false;
            var rand = new Random();
            while (!uniqueReceipt)
            {
                receiptNumber = $"{receiptPrefix}{DateTime.UtcNow:yyMMddHHmmss}-{rand.Next(1000, 9999)}";
                var exists = await _context.Payments.AnyAsync(p => p.ReceiptNumber == receiptNumber);
                if (!exists) uniqueReceipt = true;
            }

            var finalAmount = renewDto.Amount - renewDto.Discount;

            var payment = new Payment
            {
                MemberID = renewDto.MemberID,
                SubscriptionID = sub.SubscriptionID,
                Amount = renewDto.Amount,
                Discount = renewDto.Discount,
                FinalAmount = finalAmount,
                PaymentMethod = method,
                PaymentStatus = PaymentStatus.Paid,
                ReceiptNumber = receiptNumber,
                ReferenceNumber = renewDto.ReferenceNumber,
                DatePaid = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            await _paymentRepository.AddAsync(payment);

            await _auditService.LogActivityAsync(
                GetCurrentUserId(),
                "Subscription Renewed",
                $"Subscription ID {sub.SubscriptionID} for member {member.FirstName} {member.LastName} (ID: {member.MemberID}) renewed via payment {receiptNumber}.",
                GetClientIpAddress()
            );

            await transaction.CommitAsync();

            await _eventPublisher.PublishAsync(new PaymentReceivedEvent
            {
                PaymentId = payment.PaymentID,
                MemberId = payment.MemberID,
                MemberEmail = member.Email ?? string.Empty,
                Amount = payment.FinalAmount,
                ReceiptNumber = payment.ReceiptNumber
            });

            sub.Member = member;
            sub.Plan = plan;

            return MapToDto(sub);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static SubscriptionResponseDto MapToDto(Subscription sub)
    {
        return new SubscriptionResponseDto
        {
            SubscriptionID = sub.SubscriptionID,
            MemberID = sub.MemberID,
            MemberName = sub.Member != null ? $"{sub.Member.FirstName} {sub.Member.LastName}" : "Unknown",
            PlanID = sub.PlanID,
            PlanName = sub.Plan?.PlanName ?? "Unknown Plan",
            StartDate = sub.StartDate,
            EndDate = sub.EndDate,
            Status = sub.Status,
            LastModified = sub.LastModified
        };
    }
}
