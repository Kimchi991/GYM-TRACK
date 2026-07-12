using System.Security.Cryptography;
using System.Text;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Events.Membership;
using GymTrackPro.Shared.Events.Payments;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepository;
    private readonly IMembershipPlanRepository _planRepository;
    private readonly GymDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDomainEventPublisher _eventPublisher;
    private readonly ISystemSettingService _settingsService;
    private readonly IClockService _clock;
    private readonly ITimezoneService _timezoneService;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepository,
        IMemberRepository memberRepository,
        IMembershipPlanRepository planRepository,
        GymDbContext context,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserContext currentUser,
        IDomainEventPublisher eventPublisher,
        IPaymentRepository paymentRepository,
        ISystemSettingService settingsService,
        IClockService clock,
        ITimezoneService timezoneService)
    {
        _subscriptionRepository = subscriptionRepository;
        _ = memberRepository;
        _planRepository = planRepository;
        _context = context;
        _ = auditService;
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _eventPublisher = eventPublisher;
        _ = paymentRepository;
        _settingsService = settingsService;
        _clock = clock;
        _timezoneService = timezoneService;
    }

    public async Task<SubscriptionResponseDto?> GetByIdAsync(int id)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(id);
        return subscription is null ? null : MapToDto(subscription);
    }

    public async Task<IEnumerable<SubscriptionResponseDto>> GetByMemberIdAsync(int memberId)
    {
        var subscriptions = await _subscriptionRepository.GetByMemberIdAsync(memberId);
        return subscriptions.Select(MapToDto);
    }

    public async Task<SubscriptionResponseDto> SubscribeMemberAsync(CreateSubscriptionDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        var startDate = GymMembershipPolicy.RequireCalendarInput(
            request.StartDate,
            nameof(request.StartDate));
        if (GymMembershipPolicy.ToCalendarDate(startDate) < currentGymDate)
        {
            throw InvalidMembership(
                "A pending subscription must start on or after the current gym date.");
        }

        var planSnapshot = await _planRepository.GetByIdAsync(request.PlanID)
            ?? throw new KeyNotFoundException("Membership plan not found.");
        RequireActivePlan(planSnapshot);
        var endDate = GymMembershipPolicy.CalculateInclusiveEnd(
            startDate,
            planSnapshot.DurationDays);
        var key = new SubscriptionCreateKey(
            request.MemberID,
            request.PlanID,
            startDate,
            endDate,
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
                _ = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                RequireActiveMember(member);
                var plan = await _context.MembershipPlans.SingleOrDefaultAsync(
                    item => item.PlanID == operationKey.PlanId,
                    cancellationToken)
                    ?? throw new KeyNotFoundException("Membership plan not found.");
                RequireActivePlan(plan);
                if (plan.DurationDays != planSnapshot.DurationDays)
                {
                    throw MembershipConflict("The membership plan changed during the request.");
                }

                EnsureNoBlockingOverlap(
                    subscriptions,
                    operationKey.StartDate,
                    operationKey.EndDate);

                var subscription = new Subscription
                {
                    MemberID = operationKey.MemberId,
                    PlanID = operationKey.PlanId,
                    StartDate = operationKey.StartDate,
                    EndDate = operationKey.EndDate,
                    Status = GymMembershipPolicy.PendingPayment,
                    LastModified = operationKey.TimestampUtc,
                    Member = member,
                    Plan = plan
                };
                _context.Subscriptions.Add(subscription);
                AddAudit(
                    actorUserId,
                    "Subscription Initialized",
                    $"Subscription initialized for member ID {member!.MemberID}, plan ID {plan.PlanID}. Status: {GymMembershipPolicy.PendingPayment}.",
                    operationKey.TimestampUtc);
                return subscription;
            },
            (operationKey, cancellationToken) => _context.Subscriptions
                .AsNoTracking()
                .AnyAsync(subscription => subscription.MemberID == operationKey.MemberId
                    && subscription.PlanID == operationKey.PlanId
                    && subscription.StartDate == operationKey.StartDate
                    && subscription.EndDate == operationKey.EndDate
                    && subscription.Status == GymMembershipPolicy.PendingPayment
                    && subscription.LastModified == operationKey.TimestampUtc,
                    cancellationToken));

        return MapToDto(result);
    }

    public async Task PauseSubscriptionAsync(int subscriptionID, string reason)
    {
        var normalizedReason = reason?.Trim().Normalize(NormalizationForm.FormKC);
        if (string.IsNullOrWhiteSpace(normalizedReason)
            || normalizedReason.Length > 255
            || normalizedReason.Any(char.IsControl))
        {
            throw InvalidMembership("A pause reason between 1 and 255 characters is required.");
        }

        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        var memberId = await GetSubscriptionMemberIdAsync(subscriptionID);
        var key = new SubscriptionTransitionKey(subscriptionID, memberId, nowUtc);
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
                _ = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                RequireActiveMember(member);
                var subscription = subscriptions.SingleOrDefault(item =>
                    item.SubscriptionID == operationKey.SubscriptionId)
                    ?? throw new KeyNotFoundException("Subscription not found.");
                if (!GymMembershipPolicy.Covers(subscription, currentGymDate))
                {
                    throw MembershipConflict(
                        "Only a membership covering the current gym date can be paused.");
                }

                if (!string.Equals(subscription.Status, GymMembershipPolicy.Active, StringComparison.Ordinal))
                {
                    throw MembershipConflict("Only active subscriptions can be paused.");
                }

                if (await _context.MembershipPauses.AnyAsync(
                        pause => pause.SubscriptionID == subscription.SubscriptionID
                            && pause.PauseEndDate == null,
                        cancellationToken))
                {
                    throw MembershipConflict("The subscription already has an open pause.");
                }

                _context.MembershipPauses.Add(new MembershipPause
                {
                    SubscriptionID = subscription.SubscriptionID,
                    PauseStartDate = operationKey.TimestampUtc,
                    Reason = normalizedReason,
                    DateCreated = operationKey.TimestampUtc
                });
                subscription.Status = GymMembershipPolicy.Paused;
                subscription.LastModified = operationKey.TimestampUtc;
                AddAudit(
                    actorUserId,
                    "Subscription Paused",
                    $"Subscription ID {subscription.SubscriptionID} paused.",
                    operationKey.TimestampUtc);
                return subscription;
            },
            async (operationKey, cancellationToken) =>
                await _context.Subscriptions.AsNoTracking().AnyAsync(
                    subscription => subscription.SubscriptionID == operationKey.SubscriptionId
                        && subscription.Status == GymMembershipPolicy.Paused
                        && subscription.LastModified == operationKey.TimestampUtc,
                    cancellationToken)
                && await _context.MembershipPauses.AsNoTracking().AnyAsync(
                    pause => pause.SubscriptionID == operationKey.SubscriptionId
                        && pause.PauseStartDate == operationKey.TimestampUtc
                        && pause.PauseEndDate == null,
                    cancellationToken));

        await PublishPausedAsync(result);
    }

    public async Task ResumeSubscriptionAsync(int subscriptionID)
    {
        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync();
        var memberId = await GetSubscriptionMemberIdAsync(subscriptionID);
        var key = new SubscriptionTransitionKey(subscriptionID, memberId, nowUtc);
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
                _ = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                RequireActiveMember(member);
                var subscription = subscriptions.SingleOrDefault(item =>
                    item.SubscriptionID == operationKey.SubscriptionId)
                    ?? throw new KeyNotFoundException("Subscription not found.");
                if (!string.Equals(subscription.Status, GymMembershipPolicy.Paused, StringComparison.Ordinal))
                {
                    throw MembershipConflict("Only paused subscriptions can be resumed.");
                }

                var openPauses = await _context.MembershipPauses
                    .Where(pause => pause.SubscriptionID == subscription.SubscriptionID
                        && pause.PauseEndDate == null)
                    .ToListAsync(cancellationToken);
                if (openPauses.Count != 1)
                {
                    throw MembershipConflict(
                        "The subscription must have exactly one open pause to resume.");
                }

                var openPause = openPauses[0];
                var pauseStartUtc = AsUtc(openPause.PauseStartDate);
                var pauseStartGymDate = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(pauseStartUtc, timeZone));
                var subscriptionStart = GymMembershipPolicy.ToCalendarDate(subscription.StartDate);
                var subscriptionEnd = GymMembershipPolicy.ToCalendarDate(subscription.EndDate);
                if (pauseStartGymDate < subscriptionStart
                    || pauseStartGymDate > subscriptionEnd
                    || pauseStartGymDate > currentGymDate)
                {
                    throw MembershipConflict(
                        "The open pause start must be within the original membership coverage and cannot be in the future.");
                }

                var pausedDays = Math.Max(
                    1,
                    currentGymDate.DayNumber - pauseStartGymDate.DayNumber);
                openPause.PauseEndDate = operationKey.TimestampUtc;
                DateTime extendedEndDate;
                try
                {
                    extendedEndDate = GymMembershipPolicy.NormalizeCalendarDate(
                        subscription.EndDate.AddDays(pausedDays));
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw InvalidMembership("The resumed membership exceeds the supported calendar range.");
                }

                if (subscriptions.Any(other => other.SubscriptionID != subscription.SubscriptionID
                    && GymMembershipPolicy.IsBlockingStatus(other.Status)
                    && GymMembershipPolicy.Overlaps(
                        other.StartDate,
                        other.EndDate,
                        subscription.StartDate,
                        extendedEndDate)))
                {
                    throw MembershipConflict(
                        "The resumed membership would overlap another blocking subscription.");
                }

                subscription.EndDate = extendedEndDate;

                subscription.Status = GymMembershipPolicy.Active;
                subscription.LastModified = operationKey.TimestampUtc;
                AddAudit(
                    actorUserId,
                    "Subscription Resumed",
                    $"Subscription ID {subscription.SubscriptionID} resumed and extended by {pausedDays} gym day(s).",
                    operationKey.TimestampUtc);
                return subscription;
            },
            async (operationKey, cancellationToken) =>
                await _context.Subscriptions.AsNoTracking().AnyAsync(
                    subscription => subscription.SubscriptionID == operationKey.SubscriptionId
                        && subscription.Status == GymMembershipPolicy.Active
                        && subscription.LastModified == operationKey.TimestampUtc,
                    cancellationToken)
                && await _context.MembershipPauses.AsNoTracking().AnyAsync(
                    pause => pause.SubscriptionID == operationKey.SubscriptionId
                        && pause.PauseEndDate == operationKey.TimestampUtc,
                    cancellationToken));

        await PublishResumedAsync(result);
    }

    public async Task<SubscriptionResponseDto> RenewSubscriptionAsync(RenewSubscriptionDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actorUserId = GetRequiredCurrentUserId();
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        var requestedStart = GymMembershipPolicy.RequireCalendarInput(
            request.StartDate,
            nameof(request.StartDate));
        var planSnapshot = await _planRepository.GetByIdAsync(request.PlanID)
            ?? throw new KeyNotFoundException("Membership plan not found.");
        RequireActivePlan(planSnapshot);
        ValidatePlanPayment(request.Amount, request.Discount, planSnapshot.Price);
        var paymentMethod = ParsePaymentMethod(request.PaymentMethod);
        var referenceNumber = NormalizeReference(paymentMethod, request.ReferenceNumber);
        var receiptPrefix = await _settingsService.GetValueAsync("ReceiptPrefix", "REC-");
        var receiptNumber = GenerateReceiptNumber(receiptPrefix, nowUtc);
        var key = new RenewalKey(
            request.MemberID,
            request.PlanID,
            requestedStart,
            request.Amount,
            request.Discount,
            paymentMethod,
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
                _ = await GymMembershipTransaction.LockMemberPaymentsAsync(
                    _context,
                    operationKey.MemberId,
                    cancellationToken);
                RequireActiveMember(member);
                var plan = await _context.MembershipPlans.SingleOrDefaultAsync(
                    item => item.PlanID == operationKey.PlanId,
                    cancellationToken)
                    ?? throw new KeyNotFoundException("Membership plan not found.");
                RequireActivePlan(plan);
                if (plan.DurationDays != planSnapshot.DurationDays
                    || plan.Price != planSnapshot.Price)
                {
                    throw MembershipConflict("The membership plan changed during the request.");
                }

                ValidatePlanPayment(
                    operationKey.Amount,
                    operationKey.Discount,
                    plan.Price);

                var effectiveStart = CalculateRenewalStart(
                    subscriptions,
                    operationKey.RequestedStart,
                    currentGymDate);
                var endDate = GymMembershipPolicy.CalculateInclusiveEnd(
                    effectiveStart,
                    plan.DurationDays);
                EnsureNoBlockingOverlap(subscriptions, effectiveStart, endDate);
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

                var subscription = new Subscription
                {
                    MemberID = operationKey.MemberId,
                    PlanID = operationKey.PlanId,
                    StartDate = effectiveStart,
                    EndDate = endDate,
                    Status = GymMembershipPolicy.Active,
                    LastModified = operationKey.TimestampUtc,
                    Member = member,
                    Plan = plan
                };
                var payment = new Payment
                {
                    MemberID = operationKey.MemberId,
                    Subscription = subscription,
                    Amount = operationKey.Amount,
                    Discount = operationKey.Discount,
                    FinalAmount = operationKey.Amount - operationKey.Discount,
                    PaymentMethod = operationKey.PaymentMethod,
                    PaymentStatus = PaymentStatus.Paid,
                    ReceiptNumber = operationKey.ReceiptNumber,
                    ReferenceNumber = operationKey.ReferenceNumber,
                    DatePaid = operationKey.TimestampUtc,
                    LastModified = operationKey.TimestampUtc,
                    Member = member
                };
                _context.AddRange(subscription, payment);
                AddAudit(
                    actorUserId,
                    "Subscription Renewed",
                    $"Member ID {member!.MemberID} renewed plan ID {plan.PlanID} via receipt {payment.ReceiptNumber}.",
                    operationKey.TimestampUtc);
                return new RenewalResult(subscription, payment, member, WasReplay: false);
            },
            (operationKey, cancellationToken) => _context.Payments
                .AsNoTracking()
                .AnyAsync(payment => payment.ReceiptNumber == operationKey.ReceiptNumber
                    && payment.MemberID == operationKey.MemberId
                    && payment.Amount == operationKey.Amount
                    && payment.Discount == operationKey.Discount
                    && payment.PaymentMethod == operationKey.PaymentMethod
                    && payment.PaymentStatus == PaymentStatus.Paid
                    && payment.DatePaid == operationKey.TimestampUtc,
                    cancellationToken));

        if (!result.WasReplay)
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

        result.Subscription.Member = result.Member;
        result.Subscription.Plan ??= planSnapshot;
        return MapToDto(result.Subscription);
    }

    private async Task PublishPausedAsync(Subscription subscription)
    {
        var data = await LoadEventDataAsync(subscription);
        await _eventPublisher.PublishAsync(new MembershipPausedEvent
        {
            SubscriptionId = subscription.SubscriptionID,
            MemberId = subscription.MemberID,
            MemberEmail = data.Member?.Email ?? string.Empty,
            PlanName = data.Plan?.PlanName ?? "Unknown"
        });
    }

    private async Task PublishResumedAsync(Subscription subscription)
    {
        var data = await LoadEventDataAsync(subscription);
        await _eventPublisher.PublishAsync(new MembershipResumedEvent
        {
            SubscriptionId = subscription.SubscriptionID,
            MemberId = subscription.MemberID,
            MemberEmail = data.Member?.Email ?? string.Empty,
            PlanName = data.Plan?.PlanName ?? "Unknown"
        });
    }

    private async Task<(Member? Member, MembershipPlan? Plan)> LoadEventDataAsync(
        Subscription subscription)
    {
        var member = subscription.Member ?? await _context.Members
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.MemberID == subscription.MemberID);
        var plan = subscription.Plan ?? await _context.MembershipPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.PlanID == subscription.PlanID);
        return (member, plan);
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

    private static void EnsureNoBlockingOverlap(
        IEnumerable<Subscription> subscriptions,
        DateTime startDate,
        DateTime endDate)
    {
        if (subscriptions.Any(subscription => GymMembershipPolicy.IsBlockingStatus(subscription.Status)
            && GymMembershipPolicy.Overlaps(
                subscription.StartDate,
                subscription.EndDate,
                startDate,
                endDate)))
        {
            throw MembershipConflict("The membership window overlaps an existing blocking subscription.");
        }
    }

    private static DateTime CalculateRenewalStart(
        IEnumerable<Subscription> subscriptions,
        DateTime requestedStart,
        DateOnly currentGymDate)
    {
        var earliestPermittedStart = GymMembershipPolicy.ToStorageDate(currentGymDate);
        var latestBlockingEnd = subscriptions
            .Where(subscription => GymMembershipPolicy.IsBlockingStatus(subscription.Status))
            .Select(subscription => GymMembershipPolicy.NormalizeCalendarDate(subscription.EndDate))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        DateTime firstUnblockedStart;
        try
        {
            firstUnblockedStart = latestBlockingEnd == DateTime.MinValue
                ? DateTime.MinValue
                : latestBlockingEnd.AddDays(1);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidMembership("The renewed membership exceeds the supported calendar range.");
        }

        return new[]
        {
            requestedStart,
            earliestPermittedStart,
            firstUnblockedStart
        }.Max();
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

    private static void RequireActivePlan(MembershipPlan plan)
    {
        if (!string.Equals(plan.Status, GymMembershipPolicy.PlanActive, StringComparison.Ordinal)
            || plan.DurationDays < 1
            || plan.Price <= 0)
        {
            throw MembershipConflict(
                "An active membership plan with a positive duration and price is required.");
        }
    }

    private static PaymentMethod ParsePaymentMethod(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !Enum.GetNames<PaymentMethod>().Any(name =>
                string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
            || !Enum.TryParse<PaymentMethod>(normalized, true, out var method))
        {
            throw InvalidPayment("The payment method is invalid.");
        }

        return method;
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

    private async Task<int> GetSubscriptionMemberIdAsync(int subscriptionId)
    {
        var memberId = await _context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.SubscriptionID == subscriptionId)
            .Select(subscription => (int?)subscription.MemberID)
            .SingleOrDefaultAsync();
        return memberId ?? throw new KeyNotFoundException("Subscription not found.");
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

    private static SubscriptionResponseDto MapToDto(Subscription subscription)
    {
        return new SubscriptionResponseDto
        {
            SubscriptionID = subscription.SubscriptionID,
            MemberID = subscription.MemberID,
            MemberName = subscription.Member is null
                ? "Unknown"
                : $"{subscription.Member.FirstName} {subscription.Member.LastName}",
            PlanID = subscription.PlanID,
            PlanName = subscription.Plan?.PlanName ?? "Unknown Plan",
            StartDate = GymMembershipPolicy.NormalizeCalendarDate(subscription.StartDate),
            EndDate = GymMembershipPolicy.NormalizeCalendarDate(subscription.EndDate),
            Status = subscription.Status,
            LastModified = AsUtc(subscription.LastModified)
        };
    }

    private static AppAccessException InvalidMembership(string message) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.MembershipDateInvalid,
        message);

    private static AppAccessException MembershipConflict(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.MembershipConflict,
        message);

    private static AppAccessException InvalidPayment(string message) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.PaymentInvalid,
        message);

    private static AppAccessException PaymentConflict(string message) => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.PaymentConflict,
        message);

    private sealed record SubscriptionCreateKey(
        int MemberId,
        int PlanId,
        DateTime StartDate,
        DateTime EndDate,
        DateTime TimestampUtc);

    private sealed record SubscriptionTransitionKey(
        int SubscriptionId,
        int MemberId,
        DateTime TimestampUtc);

    private sealed record RenewalKey(
        int MemberId,
        int PlanId,
        DateTime RequestedStart,
        decimal Amount,
        decimal Discount,
        PaymentMethod PaymentMethod,
        string? ReferenceNumber,
        string ReceiptNumber,
        DateTime TimestampUtc);

    private sealed record RenewalResult(
        Subscription Subscription,
        Payment Payment,
        Member Member,
        bool WasReplay);
}
