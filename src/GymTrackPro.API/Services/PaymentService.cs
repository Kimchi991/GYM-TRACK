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
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

using GymTrackPro.Shared.Events.Payments;

namespace GymTrackPro.API.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IMemberRepository _memberRepository;
    private readonly GymDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISystemSettingService _settingsService;
    private readonly IDomainEventPublisher _eventPublisher;

    public PaymentService(
        IPaymentRepository paymentRepository,
        ISubscriptionRepository subscriptionRepository,
        IMemberRepository memberRepository,
        GymDbContext context,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ISystemSettingService settingsService,
        IDomainEventPublisher eventPublisher)
    {
        _paymentRepository = paymentRepository;
        _subscriptionRepository = subscriptionRepository;
        _memberRepository = memberRepository;
        _context = context;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _settingsService = settingsService;
        _eventPublisher = eventPublisher;
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

    public async Task<PaymentResponseDto?> GetByIdAsync(int id)
    {
        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null) return null;

        return MapToDto(payment);
    }

    public async Task<IEnumerable<PaymentResponseDto>> GetByMemberIdAsync(int memberId)
    {
        var payments = await _paymentRepository.GetByMemberIdAsync(memberId);
        return payments.Select(MapToDto);
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(CreatePaymentDto paymentDto)
    {
        // BR-05: Amounts cannot be negative
        if (paymentDto.Amount < 0 || paymentDto.Discount < 0)
        {
            throw new ArgumentException("Payment amount and discount cannot be negative.");
        }

        var finalAmount = paymentDto.Amount - paymentDto.Discount;
        if (finalAmount < 0)
        {
            throw new ArgumentException("Discount cannot exceed the base payment amount.");
        }

        // Verify member and subscription
        var member = await _memberRepository.GetByIdAsync(paymentDto.MemberID);
        if (member == null)
        {
            throw new KeyNotFoundException("Member not found.");
        }

        var sub = await _subscriptionRepository.GetByIdAsync(paymentDto.SubscriptionID);
        if (sub == null)
        {
            throw new KeyNotFoundException("Subscription not found.");
        }

        // Parse Enums
        if (!Enum.TryParse<PaymentMethod>(paymentDto.PaymentMethod, true, out var method))
        {
            throw new ArgumentException($"Invalid payment method: {paymentDto.PaymentMethod}");
        }

        if (!Enum.TryParse<PaymentStatus>(paymentDto.PaymentStatus, true, out var status))
        {
            throw new ArgumentException($"Invalid payment status: {paymentDto.PaymentStatus}");
        }

        // BR-03: Reference numbers are unique for online payments
        if (method != PaymentMethod.Cash)
        {
            if (string.IsNullOrWhiteSpace(paymentDto.ReferenceNumber))
            {
                throw new ArgumentException("Reference number is required for online payments.");
            }

            var duplicateRef = await _context.Payments
                .AnyAsync(p => p.ReferenceNumber == paymentDto.ReferenceNumber && !p.IsDeleted);

            if (duplicateRef)
            {
                throw new ArgumentException("A payment transaction with this reference number already exists.");
            }
        }

        // BR-02: Receipt numbers are unique (prefix retrieved dynamically from settings)
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

        var payment = new Payment
        {
            MemberID = paymentDto.MemberID,
            SubscriptionID = paymentDto.SubscriptionID,
            Amount = paymentDto.Amount,
            Discount = paymentDto.Discount,
            FinalAmount = finalAmount,
            PaymentMethod = method,
            PaymentStatus = status,
            ReceiptNumber = receiptNumber,
            ReferenceNumber = paymentDto.ReferenceNumber,
            DatePaid = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };

        // Save payment
        await _paymentRepository.AddAsync(payment);

        // BR-01: Update subscription status if payment is Paid
        if (status == PaymentStatus.Paid)
        {
            sub.Status = "Active";
            sub.LastModified = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(sub);

            await _auditService.LogActivityAsync(
                GetCurrentUserId(),
                "Subscription Activated",
                $"Subscription ID: {sub.SubscriptionID} activated via successful payment {receiptNumber}.",
                GetClientIpAddress()
            );
        }

        // BR-06: Every payment generates audit logs
        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "Payment Completed",
            $"Payment of {finalAmount:C} processed for member {member.FirstName} {member.LastName} (ID: {member.MemberID}). Receipt: {receiptNumber}.",
            GetClientIpAddress()
        );

        // Publish Domain Event if payment is Paid
        if (status == PaymentStatus.Paid)
        {
            await _eventPublisher.PublishAsync(new PaymentReceivedEvent
            {
                PaymentId = payment.PaymentID,
                MemberId = payment.MemberID,
                MemberEmail = member.Email ?? string.Empty,
                Amount = payment.FinalAmount,
                ReceiptNumber = payment.ReceiptNumber
            });
        }

        // Load navigations
        payment.Member = member;
        payment.Subscription = sub;

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> RefundPaymentAsync(int id)
    {
        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null)
        {
            throw new KeyNotFoundException("Payment record not found.");
        }

        // BR-07: Completed payments are immutable, can only be refunded
        if (payment.PaymentStatus == PaymentStatus.Refunded)
        {
            throw new InvalidOperationException("This payment transaction is already refunded.");
        }

        payment.PaymentStatus = PaymentStatus.Refunded;
        payment.LastModified = DateTime.UtcNow;

        await _paymentRepository.UpdateAsync(payment);

        // Optionally, update the associated subscription status back to PendingPayment or Suspended/Cancelled
        if (payment.Subscription != null)
        {
            payment.Subscription.Status = "Cancelled";
            payment.Subscription.LastModified = DateTime.UtcNow;
            await _subscriptionRepository.UpdateAsync(payment.Subscription);
        }

        // Log refund action
        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "Payment Refunded",
            $"Payment ID: {id} (Receipt: {payment.ReceiptNumber}) has been refunded.",
            GetClientIpAddress()
        );

        // Publish Domain Event
        await _eventPublisher.PublishAsync(new RefundProcessedEvent
        {
            PaymentId = payment.PaymentID,
            MemberId = payment.MemberID,
            MemberEmail = payment.Member?.Email ?? string.Empty,
            Amount = payment.FinalAmount,
            ReceiptNumber = payment.ReceiptNumber
        });

        return MapToDto(payment);
    }

    public async Task<IEnumerable<PaymentResponseDto>> SearchPaymentsAsync(
        DateTime? date,
        string? method,
        string? status,
        int? memberId,
        string? receiptNumber)
    {
        var query = _context.Payments
            .Include(p => p.Member)
            .Include(p => p.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(p => !p.IsDeleted);

        if (date.HasValue)
        {
            var dateOnly = date.Value.Date;
            query = query.Where(p => p.DatePaid.Date == dateOnly);
        }

        if (!string.IsNullOrWhiteSpace(method) && Enum.TryParse<PaymentMethod>(method, true, out var pMethod))
        {
            query = query.Where(p => p.PaymentMethod == pMethod);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var pStatus))
        {
            query = query.Where(p => p.PaymentStatus == pStatus);
        }

        if (memberId.HasValue)
        {
            query = query.Where(p => p.MemberID == memberId.Value);
        }

        if (!string.IsNullOrWhiteSpace(receiptNumber))
        {
            query = query.Where(p => p.ReceiptNumber.Contains(receiptNumber));
        }

        var results = await query.ToListAsync();
        return results.Select(MapToDto);
    }

    private static PaymentResponseDto MapToDto(Payment payment)
    {
        return new PaymentResponseDto
        {
            PaymentID = payment.PaymentID,
            MemberID = payment.MemberID,
            MemberName = payment.Member != null ? $"{payment.Member.FirstName} {payment.Member.LastName}" : "Unknown Member",
            SubscriptionID = payment.SubscriptionID,
            PlanName = payment.Subscription?.Plan?.PlanName ?? "Unknown Plan",
            Amount = payment.Amount,
            Discount = payment.Discount,
            FinalAmount = payment.FinalAmount,
            PaymentMethod = payment.PaymentMethod.ToString(),
            PaymentStatus = payment.PaymentStatus.ToString(),
            ReceiptNumber = payment.ReceiptNumber,
            ReferenceNumber = payment.ReferenceNumber,
            DatePaid = payment.DatePaid,
            LastModified = payment.LastModified
        };
    }
}
