using System.Security.Cryptography;
using System.Text;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly GymDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemSettingService _settingsService;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly IClockService _clock;
    private readonly ITimezoneService _timezoneService;

    public PaymentService(
        IPaymentRepository paymentRepository,
        ISubscriptionRepository subscriptionRepository,
        IMemberRepository memberRepository,
        GymDbContext context,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserContext currentUser,
        ISystemSettingService settingsService,
        IDomainEventPublisher eventPublisher,
        IClockService clock,
        ITimezoneService timezoneService)
    {
        _paymentRepository = paymentRepository;
        _ = subscriptionRepository;
        _ = memberRepository;
        _context = context;
        _ = auditService;
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _settingsService = settingsService;
        _eventPublisher = eventPublisher;
        _clock = clock;
        _timezoneService = timezoneService;
    }

    public async Task<PaymentResponseDto?> GetByIdAsync(int id)
    {
        var payment = await _paymentRepository.GetByIdAsync(id);
        return payment is null ? null : MapToDto(payment);
    }

    public async Task<IEnumerable<PaymentResponseDto>> GetByMemberIdAsync(int memberId)
    {
        var payments = await _paymentRepository.GetByMemberIdAsync(memberId);
        return payments.Select(MapToDto);
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(CreatePaymentDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        ValidateAmounts(request.Amount, request.Discount);
        var method = ParsePaymentMethod(request.PaymentMethod);
        var status = ParsePaymentStatus(request.PaymentStatus);
        var referenceNumber = NormalizeReference(method, request.ReferenceNumber);
        var prefix = await _settingsService.GetValueAsync("ReceiptPrefix", "REC-");
        var receiptNumber = GenerateReceiptNumber(prefix, nowUtc);
        var key = new PaymentCreateKey(
            request.MemberID,
            request.SubscriptionID,
            request.Amount,
            request.Discount,
            method,
            status,
            referenceNumber,
            receiptNumber,
            nowUtc);

        var result = await GymMembershipTransaction.ExecuteVerifiedAsync(
            _context,
            key,
            async (operationKey, cancellationToken) =>
            {
                var member = await GymMembershipTransaction.LockMemberAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                var subscriptions = await GymMembershipTransaction.LockMemberSubscriptionsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                var payments = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                RequireActiveMember(member);
                if (operationKey.ReferenceNumber is not null)
                {
                    var replay = FindReferenceReplay(
                        operationKey,
                        payments,
                        subscriptions,
                        member!);
                    if (replay is not null)
                    {
                        return replay;
                    }
                }

                var subscription = subscriptions.SingleOrDefault(item =>
                    item.SubscriptionID == operationKey.SubscriptionId);
                if (subscription is null || subscription.MemberID != operationKey.MemberId)
                {
                    throw PaymentConflict("The subscription does not belong to the requested member.");
                }

                if (!IsNormalizedWindow(subscription))
                {
                    throw MembershipConflict("The subscription calendar window is not normalized.");
                }

                if (GymMembershipPolicy.ToCalendarDate(subscription.EndDate) < currentGymDate)
                {
                    throw PaymentConflict("An expired subscription window cannot be funded.");
                }

                var plan = await _context.MembershipPlans.SingleOrDefaultAsync(
                    item => item.PlanID == subscription.PlanID,
                    cancellationToken)
                    ?? throw PaymentConflict("The subscription plan is unavailable.");
                RequireFundablePlan(plan);
                ValidatePlanPayment(
                    operationKey.Amount,
                    operationKey.Discount,
                    plan.Price);
                subscription.Plan = plan;

                if (!string.Equals(
                        subscription.Status,
                        GymMembershipPolicy.PendingPayment,
                        StringComparison.Ordinal))
                {
                    throw PaymentConflict(
                        "Only a pending-payment subscription can receive a new funding attempt.");
                }

                if (operationKey.Status == PaymentStatus.Paid)
                {
                    if (payments.Any(payment => !payment.IsDeleted
                        && payment.SubscriptionID == subscription.SubscriptionID
                        && payment.PaymentStatus == PaymentStatus.Paid))
                    {
                        throw PaymentConflict("The subscription already has a successful payment.");
                    }

                    if (subscriptions.Any(other => other.SubscriptionID != subscription.SubscriptionID
                        && GymMembershipPolicy.IsBlockingStatus(other.Status)
                        && GymMembershipPolicy.Overlaps(
                            other.StartDate,
                            other.EndDate,
                            subscription.StartDate,
                            subscription.EndDate)))
                    {
                        throw MembershipConflict(
                            "The subscription window overlaps another blocking subscription.");
                    }
                }

                if (await _context.Payments.AsNoTracking().AnyAsync(
                        payment => payment.ReceiptNumber == operationKey.ReceiptNumber,
                        cancellationToken))
                {
                    throw PaymentConflict("The generated receipt number is already in use.");
                }

                if (operationKey.ReferenceNumber is not null
                    && await _context.Payments.AsNoTracking().AnyAsync(
                        payment => !payment.IsDeleted
                            && payment.ReferenceNumber == operationKey.ReferenceNumber,
                        cancellationToken))
                {
                    throw PaymentConflict("The payment reference number is already in use.");
                }

                var payment = new Payment
                {
                    MemberID = operationKey.MemberId,
                    SubscriptionID = subscription.SubscriptionID,
                    Amount = operationKey.Amount,
                    Discount = operationKey.Discount,
                    FinalAmount = operationKey.Amount - operationKey.Discount,
                    PaymentMethod = operationKey.Method,
                    PaymentStatus = operationKey.Status,
                    ReceiptNumber = operationKey.ReceiptNumber,
                    ReferenceNumber = operationKey.ReferenceNumber,
                    DatePaid = operationKey.TimestampUtc,
                    LastModified = operationKey.TimestampUtc,
                    Member = member,
                    Subscription = subscription
                };
                _context.Payments.Add(payment);
                if (operationKey.Status == PaymentStatus.Paid)
                {
                    subscription.Status = GymMembershipPolicy.Active;
                    subscription.LastModified = operationKey.TimestampUtc;
                    AddAudit(
                        actorUserId,
                        "Subscription Activated",
                        $"Subscription ID {subscription.SubscriptionID} activated by receipt {payment.ReceiptNumber}.",
                        operationKey.TimestampUtc);
                }

                AddAudit(
                    actorUserId,
                    "Payment Completed",
                    $"Payment for member ID {member!.MemberID} recorded with receipt {payment.ReceiptNumber}.",
                    operationKey.TimestampUtc);
                return new PaymentMutationResult(payment, member, WasReplay: false);
            },
            async (operationKey, cancellationToken) =>
            {
                var paymentCommitted = await _context.Payments.AsNoTracking().AnyAsync(
                    payment => payment.ReceiptNumber == operationKey.ReceiptNumber
                        && payment.MemberID == operationKey.MemberId
                        && payment.SubscriptionID == operationKey.SubscriptionId
                        && payment.Amount == operationKey.Amount
                        && payment.Discount == operationKey.Discount
                        && payment.PaymentMethod == operationKey.Method
                        && payment.PaymentStatus == operationKey.Status
                        && payment.DatePaid == operationKey.TimestampUtc,
                    cancellationToken);
                if (!paymentCommitted || operationKey.Status != PaymentStatus.Paid)
                {
                    return paymentCommitted;
                }

                return await _context.Subscriptions.AsNoTracking().AnyAsync(
                    subscription => subscription.SubscriptionID == operationKey.SubscriptionId
                        && subscription.MemberID == operationKey.MemberId
                        && subscription.Status == GymMembershipPolicy.Active
                        && subscription.LastModified == operationKey.TimestampUtc,
                    cancellationToken);
            });

        if (status == PaymentStatus.Paid && !result.WasReplay)
        {
            await _eventPublisher.PublishAsync(new PaymentReceivedEvent
            {
                PaymentId = result.Payment.PaymentID,
                MemberId = result.Payment.MemberID,
                MemberEmail = result.Member.Email ?? string.Empty,
                Amount = result.Payment.FinalAmount,
                ReceiptNumber = result.Payment.ReceiptNumber
            });
        }

        await EnsurePlanLoadedAsync(result.Payment);

        return MapToDto(result.Payment);
    }

    public async Task<PaymentResponseDto> RefundPaymentAsync(int id)
    {
        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var identity = await _context.Payments
            .AsNoTracking()
            .Where(payment => payment.PaymentID == id)
            .Select(payment => new
            {
                payment.MemberID,
                payment.SubscriptionID
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException("Payment record not found.");
        var key = new RefundKey(
            id,
            identity.MemberID,
            identity.SubscriptionID,
            nowUtc);
        var result = await GymMembershipTransaction.ExecuteVerifiedAsync(
            _context,
            key,
            async (operationKey, cancellationToken) =>
            {
                // Refund is an historical correction: the member may now be inactive
                // or soft-deleted. We still lock the member first but do not re-qualify it.
                var member = await GymMembershipTransaction.LockMemberAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                var subscriptions = await GymMembershipTransaction.LockMemberSubscriptionsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                var payments = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                var subscription = subscriptions.SingleOrDefault(item =>
                    item.SubscriptionID == operationKey.SubscriptionId)
                    ?? throw PaymentConflict("The associated subscription is unavailable.");
                var payment = payments.SingleOrDefault(item =>
                    item.PaymentID == operationKey.PaymentId)
                    ?? throw new KeyNotFoundException("Payment record not found.");
                if (payment.MemberID != operationKey.MemberId
                    || payment.SubscriptionID != operationKey.SubscriptionId
                    || payment.IsDeleted
                    || payment.PaymentStatus != PaymentStatus.Paid)
                {
                    throw PaymentConflict("Only a paid, non-deleted payment can be refunded.");
                }

                if (subscription.MemberID != payment.MemberID)
                {
                    throw PaymentConflict("The payment and subscription member do not match.");
                }

                payment.PaymentStatus = PaymentStatus.Refunded;
                payment.LastModified = operationKey.TimestampUtc;
                var hasOtherSuccessfulPayment = payments.Any(other => !other.IsDeleted
                    && other.PaymentID != payment.PaymentID
                    && other.SubscriptionID == payment.SubscriptionID
                    && other.PaymentStatus == PaymentStatus.Paid);
                if (!hasOtherSuccessfulPayment
                    && GymMembershipPolicy.IsBlockingStatus(subscription.Status))
                {
                    subscription.Status = GymMembershipPolicy.Cancelled;
                    subscription.LastModified = operationKey.TimestampUtc;
                }

                AddAudit(
                    actorUserId,
                    "Payment Refunded",
                    $"Payment ID {payment.PaymentID}, receipt {payment.ReceiptNumber}, refunded.",
                    operationKey.TimestampUtc);
                payment.Member = member;
                payment.Subscription = subscription;
                return new PaymentMutationResult(
                    payment,
                    member ?? new Member { MemberID = payment.MemberID },
                    WasReplay: false);
            },
            (operationKey, cancellationToken) => _context.Payments
                .AsNoTracking()
                .AnyAsync(payment => payment.PaymentID == operationKey.PaymentId
                    && payment.PaymentStatus == PaymentStatus.Refunded
                    && payment.LastModified == operationKey.TimestampUtc,
                    cancellationToken));

        await _eventPublisher.PublishAsync(new RefundProcessedEvent
        {
            PaymentId = result.Payment.PaymentID,
            MemberId = result.Payment.MemberID,
            MemberEmail = result.Member.Email ?? string.Empty,
            Amount = result.Payment.FinalAmount,
            ReceiptNumber = result.Payment.ReceiptNumber
        });
        await EnsurePlanLoadedAsync(result.Payment);
        return MapToDto(result.Payment);
    }

    public async Task<IEnumerable<PaymentResponseDto>> SearchPaymentsAsync(
        DateTime? date,
        string? method,
        string? status,
        int? memberId,
        string? receiptNumber)
    {
        var query = _context.Payments
            .Include(payment => payment.Member)
            .Include(payment => payment.Subscription)
            .ThenInclude(subscription => subscription!.Plan)
            .Where(payment => !payment.IsDeleted);
        if (date.HasValue)
        {
            var requestedDate = date.Value.Date;
            query = query.Where(payment => payment.DatePaid.Date == requestedDate);
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            if (!TryParseNamedEnum(method, out PaymentMethod paymentMethod))
            {
                throw InvalidPayment("The payment method filter is invalid.");
            }

            query = query.Where(payment => payment.PaymentMethod == paymentMethod);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseNamedEnum(status, out PaymentStatus paymentStatus))
            {
                throw InvalidPayment("The payment status filter is invalid.");
            }

            query = query.Where(payment => payment.PaymentStatus == paymentStatus);
        }

        if (memberId.HasValue)
        {
            query = query.Where(payment => payment.MemberID == memberId.Value);
        }

        if (!string.IsNullOrWhiteSpace(receiptNumber))
        {
            query = query.Where(payment => payment.ReceiptNumber.Contains(receiptNumber));
        }

        return (await query.ToListAsync()).Select(MapToDto);
    }

    private static PaymentMutationResult? FindReferenceReplay(
        PaymentCreateKey key,
        IReadOnlyCollection<Payment> payments,
        IReadOnlyCollection<Subscription> subscriptions,
        Member member)
    {
        var matches = payments
            .Where(item => !item.IsDeleted
                && item.ReferenceNumber == key.ReferenceNumber)
            .ToList();
        if (matches.Count > 1)
        {
            throw PaymentConflict("The payment reference number is not unique.");
        }

        var payment = matches.SingleOrDefault();
        if (payment is null)
        {
            return null;
        }

        if (payment.MemberID != key.MemberId
            || payment.SubscriptionID != key.SubscriptionId
            || payment.Amount != key.Amount
            || payment.Discount != key.Discount
            || payment.PaymentMethod != key.Method
            || payment.PaymentStatus != key.Status)
        {
            throw PaymentConflict("The payment reference number belongs to another request.");
        }

        var subscription = subscriptions.SingleOrDefault(item =>
            item.SubscriptionID == payment.SubscriptionID)
            ?? throw PaymentConflict("The payment subscription is unavailable.");
        payment.Member = member;
        payment.Subscription = subscription;

        return new PaymentMutationResult(
            payment,
            member,
            WasReplay: true);
    }

    private static string GenerateReceiptNumber(string prefix, DateTime nowUtc)
    {
        const int entropyBytes = 12;
        const int maximumPrefixLength = 13;
        var normalizedPrefix = prefix?.Trim().Normalize(NormalizationForm.FormKC);
        if (prefix is null
            || prefix.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(normalizedPrefix)
            || normalizedPrefix.Length > maximumPrefixLength
            || normalizedPrefix.Any(char.IsControl))
        {
            throw InvalidPayment(
                $"The receipt prefix must contain 1 to {maximumPrefixLength} non-control characters.");
        }

        var entropy = Convert.ToHexString(RandomNumberGenerator.GetBytes(entropyBytes));
        return $"{normalizedPrefix}{nowUtc:yyMMddHHmmss}-{entropy}";
    }

    private void AddAudit(int actorUserId, string action, string details, DateTime timestampUtc)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserID = actorUserId,
            Action = action,
            Details = details,
            IPAddress = GetClientIpAddress(),
            Timestamp = timestampUtc
        });
    }

    private static bool IsNormalizedWindow(Subscription subscription)
    {
        return subscription.StartDate.TimeOfDay == TimeSpan.Zero
            && subscription.EndDate.TimeOfDay == TimeSpan.Zero
            && GymMembershipPolicy.ToCalendarDate(subscription.EndDate)
                >= GymMembershipPolicy.ToCalendarDate(subscription.StartDate);
    }

    private async Task EnsurePlanLoadedAsync(Payment payment)
    {
        if (payment.Subscription is null)
        {
            payment.Subscription = await _context.Subscriptions
                .AsNoTracking()
                .SingleOrDefaultAsync(subscription =>
                    subscription.SubscriptionID == payment.SubscriptionID);
        }

        if (payment.Subscription is not null && payment.Subscription.Plan is null)
        {
            payment.Subscription.Plan = await _context.MembershipPlans
                .AsNoTracking()
                .SingleOrDefaultAsync(plan => plan.PlanID == payment.Subscription.PlanID);
        }
    }

    private static void RequireActiveMember(Member? member)
    {
        if (!GymMembershipPolicy.IsActiveMember(member))
        {
            throw new AppAccessException(
                StatusCodes.Status409Conflict,
                ErrorCodes.MemberInactive,
                "An existing active member is required.");
        }
    }

    private static void RequireFundablePlan(MembershipPlan plan)
    {
        if (!string.Equals(plan.Status, GymMembershipPolicy.PlanActive, StringComparison.Ordinal)
            || plan.DurationDays < 1
            || plan.Price <= 0)
        {
            throw MembershipConflict(
                "An active membership plan with a positive duration and price is required.");
        }
    }

    private static void ValidatePlanPayment(
        decimal amount,
        decimal discount,
        decimal authoritativePrice)
    {
        if (amount != authoritativePrice
            || discount < 0
            || amount - discount <= 0)
        {
            throw InvalidPayment(
                "The gross amount must equal the current plan price and the final amount must be positive.");
        }
    }

    private static PaymentMethod ParsePaymentMethod(string value)
    {
        if (!TryParseNamedEnum(value, out PaymentMethod method))
        {
            throw InvalidPayment("The payment method is invalid.");
        }

        return method;
    }

    private static PaymentStatus ParsePaymentStatus(string value)
    {
        if (!TryParseNamedEnum(value, out PaymentStatus status))
        {
            throw InvalidPayment("The payment status is invalid.");
        }

        if (status is not (PaymentStatus.Paid or PaymentStatus.Pending))
        {
            throw InvalidPayment("Only Paid or Pending payment creation is supported.");
        }

        return status;
    }

    private static bool TryParseNamedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !Enum.GetNames<TEnum>().Any(name =>
                string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            result = default;
            return false;
        }

        return Enum.TryParse(normalized, true, out result);
    }

    private static string? NormalizeReference(PaymentMethod method, string? referenceNumber)
    {
        var normalized = referenceNumber?.Trim().Normalize(NormalizationForm.FormKC);
        if ((referenceNumber is not null && referenceNumber.Any(char.IsControl))
            || normalized is { Length: > 100 }
            || (normalized is not null && normalized.Any(char.IsControl)))
        {
            throw InvalidPayment("The payment reference number is invalid.");
        }
        if (method != PaymentMethod.Cash && string.IsNullOrWhiteSpace(normalized))
        {
            throw InvalidPayment("A reference number is required for non-cash payments.");
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void ValidateAmounts(decimal amount, decimal discount)
    {
        if (amount <= 0 || discount < 0 || discount > amount)
        {
            throw InvalidPayment("Payment amounts are invalid.");
        }
    }

    private int GetRequiredCurrentUserId()
    {
        return _currentUser.UserId is > 0
            ? _currentUser.UserId.Value
            : throw new UnauthorizedAccessException("An active application user is required.");
    }

    private DateTime GetUtcNow()
    {
        var nowUtc = _clock.UtcNow;
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        return nowUtc;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static PaymentResponseDto MapToDto(Payment payment)
    {
        return new PaymentResponseDto
        {
            PaymentID = payment.PaymentID,
            MemberID = payment.MemberID,
            MemberName = payment.Member is null
                ? "Unknown Member"
                : $"{payment.Member.FirstName} {payment.Member.LastName}",
            SubscriptionID = payment.SubscriptionID,
            PlanName = payment.Subscription?.Plan?.PlanName ?? "Unknown Plan",
            Amount = payment.Amount,
            Discount = payment.Discount,
            FinalAmount = payment.FinalAmount,
            PaymentMethod = payment.PaymentMethod.ToString(),
            PaymentStatus = payment.PaymentStatus.ToString(),
            ReceiptNumber = payment.ReceiptNumber,
            ReferenceNumber = payment.ReferenceNumber,
            DatePaid = AsUtc(payment.DatePaid),
            LastModified = AsUtc(payment.LastModified)
        };
    }

    private static AppAccessException InvalidPayment(string message) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.PaymentInvalid,
        message);

    private static AppAccessException PaymentConflict(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.PaymentConflict,
        message);

    private static AppAccessException MembershipConflict(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.MembershipConflict,
        message);

    private sealed record PaymentCreateKey(
        int MemberId,
        int SubscriptionId,
        decimal Amount,
        decimal Discount,
        PaymentMethod Method,
        PaymentStatus Status,
        string? ReferenceNumber,
        string ReceiptNumber,
        DateTime TimestampUtc);

    private sealed record RefundKey(
        int PaymentId,
        int MemberId,
        int SubscriptionId,
        DateTime TimestampUtc);

    private sealed record PaymentMutationResult(
        Payment Payment,
        Member Member,
        bool WasReplay);
}
