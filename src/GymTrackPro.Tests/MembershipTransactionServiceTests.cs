using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Repositories;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GymTrackPro.Tests;

public class MembershipTransactionServiceTests
{
    [Fact]
    public async Task Subscribe_uses_inclusive_duration_and_rejects_overlapping_blocking_window()
    {
        await using var fixture = await Fixture.CreateAsync();

        var created = await fixture.Subscriptions.SubscribeMemberAsync(new CreateSubscriptionDto
        {
            MemberID = 1,
            PlanID = 1,
            StartDate = new DateTime(2026, 7, 1)
        });
        var overlap = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.SubscribeMemberAsync(new CreateSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 30)
            }));

        Assert.Equal(new DateTime(2026, 7, 30), created.EndDate);
        Assert.Equal(DateTimeKind.Unspecified, created.EndDate.Kind);
        Assert.Equal(GymMembershipPolicy.PendingPayment, created.Status);
        Assert.Equal(ErrorCodes.MembershipConflict, overlap.ErrorCode);
        Assert.Single(await fixture.Context.Subscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("Inactive", false)]
    [InlineData("Active", true)]
    public async Task Subscribe_rejects_inactive_or_deleted_member(string status, bool isDeleted)
    {
        await using var fixture = await Fixture.CreateAsync(memberStatus: status, memberDeleted: isDeleted);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.SubscribeMemberAsync(new CreateSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 1)
            }));

        Assert.Equal(ErrorCodes.MemberInactive, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
    }

    [Fact]
    public async Task Renewal_starts_after_latest_blocking_end_without_losing_paid_days()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.Active));
        await fixture.Context.SaveChangesAsync();

        var renewed = await fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
        {
            MemberID = 1,
            PlanID = 1,
            StartDate = new DateTime(2026, 7, 20),
            Amount = 1000m,
            Discount = 0m,
            PaymentMethod = PaymentMethod.Cash.ToString()
        });

        Assert.Equal(new DateTime(2026, 7, 31), renewed.StartDate);
        Assert.Equal(new DateTime(2026, 8, 29), renewed.EndDate);
        Assert.Equal(2, await fixture.Context.Subscriptions.CountAsync());
        Assert.Single(await fixture.Context.Payments.ToListAsync());
        fixture.Publisher.Verify(
            publisher => publisher.PublishAsync(It.IsAny<GymTrackPro.Shared.Events.Payments.PaymentReceivedEvent>()),
            Times.Once);
    }

    [Fact]
    public async Task Same_gym_day_pause_resume_closes_pause_and_extends_one_day()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.Active));
        await fixture.Context.SaveChangesAsync();

        await fixture.Subscriptions.PauseSubscriptionAsync(10, "  medical   leave  ");
        await fixture.Subscriptions.ResumeSubscriptionAsync(10);

        var subscription = await fixture.Context.Subscriptions.SingleAsync();
        var pause = await fixture.Context.MembershipPauses.SingleAsync();
        Assert.Equal(GymMembershipPolicy.Active, subscription.Status);
        Assert.Equal(new DateTime(2026, 7, 31), subscription.EndDate);
        Assert.Equal(fixture.Now, pause.PauseEndDate);
        Assert.Equal("medical   leave", pause.Reason);
    }

    [Fact]
    public async Task Resume_rejects_extension_that_would_overlap_scheduled_subscription()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.AddRange(
            Subscription(
                10,
                1,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 30),
                GymMembershipPolicy.Paused),
            Subscription(
                11,
                1,
                new DateOnly(2026, 7, 31),
                new DateOnly(2026, 8, 29),
                GymMembershipPolicy.PendingPayment));
        fixture.Context.MembershipPauses.Add(new MembershipPause
        {
            SubscriptionID = 10,
            PauseStartDate = fixture.Now,
            Reason = "Medical leave",
            DateCreated = fixture.Now
        });
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.ResumeSubscriptionAsync(10));

        Assert.Equal(ErrorCodes.MembershipConflict, exception.ErrorCode);
        Assert.Equal(
            GymMembershipPolicy.Paused,
            (await fixture.Context.Subscriptions.SingleAsync(item => item.SubscriptionID == 10)).Status);
        Assert.Null((await fixture.Context.MembershipPauses.SingleAsync()).PauseEndDate);
    }

    [Fact]
    public async Task Payment_member_subscription_mismatch_writes_zero_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Members.Add(Member(2));
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            2,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "mismatch-ref")));

        Assert.Equal(ErrorCodes.PaymentConflict, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Exact_online_payment_replay_is_once_only_and_activation_audit_is_atomic()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        var request = PaidRequest(1, 10, "provider-reference-1");

        var first = await fixture.Payments.ProcessPaymentAsync(request);
        var replay = await fixture.Payments.ProcessPaymentAsync(request);

        Assert.Equal(first.PaymentID, replay.PaymentID);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(
            GymMembershipPolicy.Active,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync());
        fixture.Publisher.Verify(
            publisher => publisher.PublishAsync(It.IsAny<GymTrackPro.Shared.Events.Payments.PaymentReceivedEvent>()),
            Times.Once);
    }

    [Theory]
    [InlineData("Inactive", false)]
    [InlineData("Active", true)]
    public async Task Pause_requires_a_locked_active_non_deleted_member(
        string memberStatus,
        bool memberDeleted)
    {
        await using var fixture = await Fixture.CreateAsync(memberStatus, memberDeleted);
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.Active));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.PauseSubscriptionAsync(10, "Medical leave"));

        Assert.Equal(ErrorCodes.MemberInactive, exception.ErrorCode);
        Assert.Equal(
            GymMembershipPolicy.Active,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Empty(await fixture.Context.MembershipPauses.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Pause_rejects_a_subscription_whose_member_is_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            99,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.Active));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.PauseSubscriptionAsync(10, "Medical leave"));

        Assert.Equal(ErrorCodes.MemberInactive, exception.ErrorCode);
        Assert.Equal(
            GymMembershipPolicy.Active,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Empty(await fixture.Context.MembershipPauses.ToListAsync());
    }

    [Theory]
    [InlineData("Inactive", false)]
    [InlineData("Active", true)]
    public async Task Refund_is_an_explicit_historical_correction_for_inactive_or_deleted_members(
        string memberStatus,
        bool memberDeleted)
    {
        await using var fixture = await Fixture.CreateAsync(memberStatus, memberDeleted);
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.Active));
        fixture.Context.Payments.Add(PaidPayment(20, 1, 10, "REC-HISTORICAL", "historical-ref"));
        await fixture.Context.SaveChangesAsync();

        var refunded = await fixture.Payments.RefundPaymentAsync(20);

        Assert.Equal(PaymentStatus.Refunded.ToString(), refunded.PaymentStatus);
        Assert.Equal(
            GymMembershipPolicy.Cancelled,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
        fixture.Publisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<GymTrackPro.Shared.Events.Payments.RefundProcessedEvent>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(900, 0)]
    [InlineData(1100, 0)]
    [InlineData(1000, 1000)]
    public async Task Payment_rejects_zero_under_over_and_full_discount_amounts(
        int amount,
        int discount)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        var request = PaidRequest(1, 10, $"price-{amount}-{discount}");
        request.Amount = amount;
        request.Discount = discount;

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(request));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(
            GymMembershipPolicy.PendingPayment,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(900, 0)]
    [InlineData(1100, 0)]
    [InlineData(1000, 1000)]
    public async Task Renewal_rejects_zero_under_over_and_full_discount_amounts(
        int amount,
        int discount)
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 12),
                Amount = amount,
                Discount = discount,
                PaymentMethod = PaymentMethod.Cash.ToString()
            }));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(31, 1000)]
    [InlineData(30, 1200)]
    public async Task Renewal_revalidates_preloaded_plan_duration_and_price_inside_transaction(
        int changedDuration,
        int changedPrice)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Settings.Setup(item => item.GetValueAsync("ReceiptPrefix", "REC-"))
            .Returns(async () =>
            {
                fixture.Context.ChangeTracker.Clear();
                var plan = await fixture.Context.MembershipPlans.SingleAsync();
                plan.DurationDays = changedDuration;
                plan.Price = changedPrice;
                await fixture.Context.SaveChangesAsync();
                return "REC-";
            });

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 12),
                Amount = 1000m,
                Discount = 0m,
                PaymentMethod = PaymentMethod.Cash.ToString()
            }));

        Assert.Equal(ErrorCodes.MembershipConflict, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Fact]
    public async Task Inactive_plan_cannot_fund_a_pending_subscription()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.PendingPayment));
        (await fixture.Context.MembershipPlans.SingleAsync()).Status = "Inactive";
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "inactive-plan")));

        Assert.Equal(ErrorCodes.MembershipConflict, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(
            GymMembershipPolicy.PendingPayment,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Inactive_plan_cannot_be_renewed()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Context.MembershipPlans.SingleAsync()).Status = "Inactive";
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 12),
                Amount = 1000m,
                Discount = 0m,
                PaymentMethod = PaymentMethod.Cash.ToString()
            }));

        Assert.Equal(ErrorCodes.MembershipConflict, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Fact]
    public async Task Pending_payment_is_recorded_without_activating_entitlement()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        var request = PaidRequest(1, 10, "pending-attempt");
        request.PaymentStatus = PaymentStatus.Pending.ToString();

        var result = await fixture.Payments.ProcessPaymentAsync(request);

        Assert.Equal(PaymentStatus.Pending.ToString(), result.PaymentStatus);
        Assert.Equal(
            GymMembershipPolicy.PendingPayment,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
        fixture.Publisher.Verify(
            publisher => publisher.PublishAsync(
                It.IsAny<GymTrackPro.Shared.Events.Payments.PaymentReceivedEvent>()),
            Times.Never);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    public async Task Payment_uses_authoritative_gym_date_for_expired_or_current_day_windows(
        int endDay,
        bool shouldSucceed)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, endDay),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        if (shouldSucceed)
        {
            var result = await fixture.Payments.ProcessPaymentAsync(
                PaidRequest(1, 10, $"end-day-{endDay}"));
            Assert.Equal(PaymentStatus.Paid.ToString(), result.PaymentStatus);
            Assert.Equal(
                GymMembershipPolicy.Active,
                (await fixture.Context.Subscriptions.SingleAsync()).Status);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
                fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, $"end-day-{endDay}")));
            Assert.Equal(ErrorCodes.PaymentConflict, exception.ErrorCode);
            Assert.Empty(await fixture.Context.Payments.ToListAsync());
        }
    }

    [Fact]
    public async Task Payment_accepts_a_future_subscription_window()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 8, 18),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        await fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "future-window"));

        Assert.Equal(
            GymMembershipPolicy.Active,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
    }

    [Theory]
    [InlineData(15, 59, 11, true)]
    [InlineData(16, 0, 12, false)]
    public async Task Payment_expiration_changes_at_the_Manila_midnight_boundary(
        int utcHour,
        int utcMinute,
        int gymDay,
        bool shouldSucceed)
    {
        var nowUtc = Utc(2026, 7, 11, utcHour, utcMinute);
        await using var fixture = await Fixture.CreateAsync(
            nowUtc: nowUtc,
            gymDate: new DateOnly(2026, 7, gymDay));
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 11),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        if (shouldSucceed)
        {
            await fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "boundary-valid"));
            Assert.Single(await fixture.Context.Payments.ToListAsync());
        }
        else
        {
            var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
                fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "boundary-expired")));
            Assert.Equal(ErrorCodes.PaymentConflict, exception.ErrorCode);
            Assert.Empty(await fixture.Context.Payments.ToListAsync());
        }
    }

    [Fact]
    public async Task Renewal_rejects_a_wholly_expired_paid_window_when_no_blocker_can_schedule_it_forward()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 6, 1),
                Amount = 1000m,
                Discount = 0m,
                PaymentMethod = PaymentMethod.Cash.ToString()
            }));

        Assert.Equal(ErrorCodes.MembershipDateInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("1", "Paid")]
    [InlineData("GCash", "1")]
    [InlineData("Undefined", "Paid")]
    [InlineData("GCash", "Undefined")]
    public async Task Payment_rejects_numeric_or_undefined_enum_strings(string method, string status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        var request = PaidRequest(1, 10, "numeric-enum");
        request.PaymentMethod = method;
        request.PaymentStatus = status;

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(request));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Undefined")]
    public async Task Renewal_rejects_numeric_or_undefined_payment_method_string(string method)
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Subscriptions.RenewSubscriptionAsync(new RenewSubscriptionDto
            {
                MemberID = 1,
                PlanID = 1,
                StartDate = new DateTime(2026, 7, 12),
                Amount = 1000m,
                Discount = 0m,
                PaymentMethod = method
            }));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Subscriptions.ToListAsync());
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Fact]
    public async Task Noncash_reference_is_normalized_before_persistence_and_replay()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        var firstRequest = PaidRequest(1, 10, "  provider-normalized  ");
        var replayRequest = PaidRequest(1, 10, "provider-normalized");

        var first = await fixture.Payments.ProcessPaymentAsync(firstRequest);
        var replay = await fixture.Payments.ProcessPaymentAsync(replayRequest);

        Assert.Equal("provider-normalized", first.ReferenceNumber);
        Assert.Equal(first.PaymentID, replay.PaymentID);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
    }

    [Theory]
    [InlineData("provider\nreference")]
    [InlineData("provider\u0001reference")]
    public async Task Noncash_reference_rejects_control_characters(string reference)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, reference)));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Fact]
    public async Task Noncash_reference_rejects_more_than_100_normalized_characters()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, new string('x', 101))));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("not-a-method", null)]
    [InlineData(null, "1")]
    [InlineData(null, "not-a-status")]
    public async Task Search_rejects_invalid_or_numeric_filters_instead_of_returning_unfiltered_data(
        string? method,
        string? status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.Active));
        fixture.Context.Payments.Add(PaidPayment(20, 1, 10, "REC-SEARCH", "search-ref"));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.SearchPaymentsAsync(null, method, status, null, null));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
    }

    [Fact]
    public async Task Noncash_replay_requires_every_request_field_to_match()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        await fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "exact-replay"));
        var changed = PaidRequest(1, 10, "exact-replay");
        changed.Amount = 900m;

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(changed));

        Assert.Equal(ErrorCodes.PaymentConflict, exception.ErrorCode);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
    }

    [Fact]
    public async Task Provider_reference_is_global_across_members()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Members.Add(Member(2));
        fixture.Context.Subscriptions.AddRange(
            Subscription(
                10,
                1,
                new DateOnly(2026, 7, 12),
                new DateOnly(2026, 8, 10),
                GymMembershipPolicy.PendingPayment),
            Subscription(
                11,
                2,
                new DateOnly(2026, 7, 12),
                new DateOnly(2026, 8, 10),
                GymMembershipPolicy.Active));
        fixture.Context.Payments.Add(PaidPayment(
            20,
            2,
            11,
            "REC-OTHER-MEMBER",
            "global-provider-ref"));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "global-provider-ref")));

        Assert.Equal(ErrorCodes.PaymentConflict, exception.ErrorCode);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(
            GymMembershipPolicy.PendingPayment,
            (await fixture.Context.Subscriptions.SingleAsync(item => item.SubscriptionID == 10)).Status);
    }

    [Fact]
    public async Task Generated_receipt_uses_normalized_bounded_prefix_and_96_bit_entropy()
    {
        await using var fixture = await Fixture.CreateAsync(receiptPrefix: "  REC-  ");
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Payments.ProcessPaymentAsync(
            PaidRequest(1, 10, "receipt-format"));

        Assert.Matches("^REC-[0-9]{12}-[0-9A-F]{24}$", result.ReceiptNumber);
        Assert.Equal(41, result.ReceiptNumber.Length);
    }

    [Fact]
    public async Task Maximum_receipt_prefix_stays_within_database_length()
    {
        await using var fixture = await Fixture.CreateAsync(receiptPrefix: "1234567890123");
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Payments.ProcessPaymentAsync(
            PaidRequest(1, 10, "max-prefix"));

        Assert.Equal(50, result.ReceiptNumber.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678901234")]
    [InlineData("REC-\n")]
    public async Task Invalid_receipt_prefix_is_controlled_and_writes_nothing(string prefix)
    {
        await using var fixture = await Fixture.CreateAsync(receiptPrefix: prefix);
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "bad-prefix")));

        Assert.Equal(ErrorCodes.PaymentInvalid, exception.ErrorCode);
        Assert.Empty(await fixture.Context.Payments.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Same_second_receipts_are_high_entropy_and_distinct()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Members.Add(Member(2));
        fixture.Context.Subscriptions.AddRange(
            Subscription(
                10,
                1,
                new DateOnly(2026, 7, 12),
                new DateOnly(2026, 8, 10),
                GymMembershipPolicy.PendingPayment),
            Subscription(
                11,
                2,
                new DateOnly(2026, 7, 12),
                new DateOnly(2026, 8, 10),
                GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        var first = await fixture.Payments.ProcessPaymentAsync(
            PaidRequest(1, 10, "entropy-one"));
        var second = await fixture.Payments.ProcessPaymentAsync(
            PaidRequest(2, 11, "entropy-two"));

        Assert.NotEqual(first.ReceiptNumber, second.ReceiptNumber);
        Assert.Equal(fixture.Now, (await fixture.Context.Payments.FirstAsync()).DatePaid);
        Assert.All(
            await fixture.Context.Payments.ToListAsync(),
            payment => Assert.Equal(41, payment.ReceiptNumber.Length));
    }

    [Fact]
    public async Task Receipt_number_model_index_remains_globally_unique()
    {
        await using var fixture = await Fixture.CreateAsync();
        var paymentType = fixture.Context.Model.FindEntityType(typeof(Payment));
        var receiptIndex = Assert.Single(paymentType!.GetIndexes(), index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(Payment.ReceiptNumber));

        Assert.True(receiptIndex.IsUnique);
    }

    [Fact]
    public async Task Publish_failure_is_not_swallowed_and_committed_payment_remains_atomic()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();
        fixture.Publisher.Setup(publisher => publisher.PublishAsync(
                It.IsAny<GymTrackPro.Shared.Events.Payments.PaymentReceivedEvent>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "publish-failure")));

        Assert.Equal("publish failed", exception.Message);
        Assert.Single(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync());
        Assert.Equal(
            GymMembershipPolicy.Active,
            (await fixture.Context.Subscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Payment_captures_one_clock_snapshot_for_mutation_and_audits()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Subscriptions.Add(Subscription(
            10,
            1,
            new DateOnly(2026, 7, 12),
            new DateOnly(2026, 8, 10),
            GymMembershipPolicy.PendingPayment));
        await fixture.Context.SaveChangesAsync();

        await fixture.Payments.ProcessPaymentAsync(PaidRequest(1, 10, "one-clock"));

        fixture.Clock.VerifyGet(clock => clock.UtcNow, Times.Once);
        Assert.All(
            await fixture.Context.AuditLogs.ToListAsync(),
            audit => Assert.Equal(fixture.Now, audit.Timestamp));
    }

    [Fact]
    public void Service_source_enforces_canonical_lock_order_but_InMemory_does_not_prove_SQL_locking()
    {
        var root = FindWorkspaceRoot();
        var paymentSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Services",
            "PaymentService.cs"));
        var process = SliceMethod(
            paymentSource,
            "public async Task<PaymentResponseDto> ProcessPaymentAsync",
            "public async Task<PaymentResponseDto> RefundPaymentAsync");
        var refund = SliceMethod(
            paymentSource,
            "public async Task<PaymentResponseDto> RefundPaymentAsync",
            "public async Task<IEnumerable<PaymentResponseDto>> SearchPaymentsAsync");
        AssertCanonicalOrder(process);
        AssertCanonicalOrder(refund);
        Assert.True(
            process.IndexOf("FindReferenceReplay(", StringComparison.Ordinal)
                > process.IndexOf("LockMemberPaymentsAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("LockPaymentAsync", refund, StringComparison.Ordinal);
        Assert.Contains("_context.Payments.AsNoTracking().AnyAsync", process, StringComparison.Ordinal);

        var subscriptionSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Services",
            "SubscriptionService.cs"));
        AssertCanonicalOrder(SliceMethod(
            subscriptionSource,
            "public async Task PauseSubscriptionAsync",
            "public async Task ResumeSubscriptionAsync"));
        AssertCanonicalOrder(SliceMethod(
            subscriptionSource,
            "public async Task<SubscriptionResponseDto> RenewSubscriptionAsync",
            "private async Task PublishPausedAsync"));
    }

    private static Payment PaidPayment(
        int id,
        int memberId,
        int subscriptionId,
        string receiptNumber,
        string referenceNumber)
    {
        return new Payment
        {
            PaymentID = id,
            MemberID = memberId,
            SubscriptionID = subscriptionId,
            Amount = 1000m,
            Discount = 100m,
            FinalAmount = 900m,
            PaymentMethod = PaymentMethod.GCash,
            PaymentStatus = PaymentStatus.Paid,
            ReceiptNumber = receiptNumber,
            ReferenceNumber = referenceNumber,
            DatePaid = Utc(2026, 7, 12, 4, 0),
            LastModified = Utc(2026, 7, 12, 4, 0)
        };
    }

    private static void AssertCanonicalOrder(string method)
    {
        var memberLock = method.IndexOf("LockMemberAsync", StringComparison.Ordinal);
        var subscriptionLocks = method.IndexOf(
            "LockMemberSubscriptionsAsync",
            StringComparison.Ordinal);
        var paymentLocks = method.IndexOf("LockMemberPaymentsAsync", StringComparison.Ordinal);

        Assert.True(memberLock >= 0, "Member lock is missing.");
        Assert.True(subscriptionLocks > memberLock, "Subscription locks must follow the member lock.");
        Assert.True(paymentLocks > subscriptionLocks, "Payment locks must follow subscription locks.");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

    private static string FindWorkspaceRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var candidate = new DirectoryInfo(
            Path.GetDirectoryName(sourceFilePath)
                ?? throw new DirectoryNotFoundException("Test source directory is unavailable."));
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.slnx"))
                && Directory.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.API")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root could not be located.");
    }

    private static CreatePaymentDto PaidRequest(int memberId, int subscriptionId, string reference)
    {
        return new CreatePaymentDto
        {
            MemberID = memberId,
            SubscriptionID = subscriptionId,
            Amount = 1000m,
            Discount = 100m,
            PaymentMethod = PaymentMethod.GCash.ToString(),
            PaymentStatus = PaymentStatus.Paid.ToString(),
            ReferenceNumber = reference
        };
    }

    private static Subscription Subscription(
        int id,
        int memberId,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = memberId,
            PlanID = 1,
            StartDate = GymMembershipPolicy.ToStorageDate(start),
            EndDate = GymMembershipPolicy.ToStorageDate(end),
            Status = status,
            LastModified = Utc(2026, 7, 1, 0, 0)
        };
    }

    private static Member Member(int id, string status = GymMembershipPolicy.MemberActive, bool deleted = false)
    {
        return new Member
        {
            MemberID = id,
            FirstName = "Test",
            LastName = $"Member{id}",
            QRCode = $"qr-{id}",
            Status = status,
            IsDeleted = deleted
        };
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            GymDbContext context,
            SubscriptionService subscriptions,
            PaymentService payments,
            Mock<IDomainEventPublisher> publisher,
            Mock<IClockService> clock,
            Mock<ISystemSettingService> settings,
            DateTime now)
        {
            Context = context;
            Subscriptions = subscriptions;
            Payments = payments;
            Publisher = publisher;
            Clock = clock;
            Settings = settings;
            Now = now;
        }

        public GymDbContext Context { get; }
        public SubscriptionService Subscriptions { get; }
        public PaymentService Payments { get; }
        public Mock<IDomainEventPublisher> Publisher { get; }
        public Mock<IClockService> Clock { get; }
        public Mock<ISystemSettingService> Settings { get; }
        public DateTime Now { get; }

        public static async Task<Fixture> CreateAsync(
            string memberStatus = GymMembershipPolicy.MemberActive,
            bool memberDeleted = false,
            string receiptPrefix = "REC-",
            DateTime? nowUtc = null,
            DateOnly? gymDate = null)
        {
            var context = new GymDbContext(new DbContextOptionsBuilder<GymDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            var now = nowUtc ?? Utc(2026, 7, 12, 4, 0);
            context.Members.Add(Member(1, memberStatus, memberDeleted));
            context.MembershipPlans.Add(new MembershipPlan
            {
                PlanID = 1,
                PlanName = "30 Day",
                DurationDays = 30,
                Price = 1000m,
                Status = GymMembershipPolicy.PlanActive,
                LastModified = now
            });
            await context.SaveChangesAsync();

            var currentUser = new Mock<ICurrentUserContext>();
            currentUser.SetupGet(user => user.UserId).Returns(5);
            var clock = new Mock<IClockService>();
            clock.SetupGet(item => item.UtcNow).Returns(now);
            var timezone = new Mock<ITimezoneService>();
            timezone.Setup(item => item.GetGymDateAsync(
                    now,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(gymDate ?? new DateOnly(2026, 7, 12));
            timezone.Setup(item => item.GetGymTimeZoneAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
            var settings = new Mock<ISystemSettingService>();
            settings.Setup(item => item.GetValueAsync("ReceiptPrefix", "REC-"))
                .ReturnsAsync(receiptPrefix);
            var publisher = new Mock<IDomainEventPublisher>();
            publisher.Setup(item => item.PublishAsync(
                    It.IsAny<GymTrackPro.Shared.Events.Payments.PaymentReceivedEvent>()))
                .Returns(Task.CompletedTask);
            publisher.Setup(item => item.PublishAsync(
                    It.IsAny<GymTrackPro.Shared.Events.Payments.RefundProcessedEvent>()))
                .Returns(Task.CompletedTask);
            publisher.Setup(item => item.PublishAsync(
                    It.IsAny<GymTrackPro.Shared.Events.Membership.MembershipPausedEvent>()))
                .Returns(Task.CompletedTask);
            publisher.Setup(item => item.PublishAsync(
                    It.IsAny<GymTrackPro.Shared.Events.Membership.MembershipResumedEvent>()))
                .Returns(Task.CompletedTask);
            var subscriptions = new SubscriptionService(
                new SubscriptionRepository(context),
                new MemberRepository(context),
                new MembershipPlanRepository(context),
                context,
                Mock.Of<IAuditService>(),
                Mock.Of<IHttpContextAccessor>(),
                currentUser.Object,
                publisher.Object,
                new PaymentRepository(context),
                settings.Object,
                clock.Object,
                timezone.Object);
            var payments = new PaymentService(
                new PaymentRepository(context),
                new SubscriptionRepository(context),
                new MemberRepository(context),
                context,
                Mock.Of<IAuditService>(),
                Mock.Of<IHttpContextAccessor>(),
                currentUser.Object,
                settings.Object,
                publisher.Object,
                clock.Object,
                timezone.Object);
            return new Fixture(context, subscriptions, payments, publisher, clock, settings, now);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
