using GymTrackPro.API.Authentication;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Http;

namespace GymTrackPro.Tests;

public class GymMembershipPolicyTests
{
    [Fact]
    public void Thirty_day_membership_beginning_july_first_ends_july_thirtieth()
    {
        var start = new DateTime(2026, 7, 1);

        var end = GymMembershipPolicy.CalculateInclusiveEnd(start, 30);

        Assert.Equal(new DateTime(2026, 7, 30), end);
        Assert.Equal(DateTimeKind.Unspecified, end.Kind);
    }

    [Fact]
    public void Adjacent_membership_windows_do_not_overlap_but_shared_end_day_does()
    {
        var firstStart = new DateTime(2026, 7, 1);
        var firstEnd = new DateTime(2026, 7, 30);

        Assert.False(GymMembershipPolicy.Overlaps(
            firstStart,
            firstEnd,
            new DateTime(2026, 7, 31),
            new DateTime(2026, 8, 29)));
        Assert.True(GymMembershipPolicy.Overlaps(
            firstStart,
            firstEnd,
            new DateTime(2026, 7, 30),
            new DateTime(2026, 8, 28)));
    }

    [Theory]
    [MemberData(nameof(InvalidCalendarInputs))]
    public void Default_or_non_midnight_calendar_input_is_stable_bad_request(DateTime value)
    {
        var exception = Assert.Throws<AppAccessException>(() =>
            GymMembershipPolicy.RequireCalendarInput(value, "StartDate"));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.MembershipDateInvalid, exception.ErrorCode);
    }

    [Fact]
    public void Effective_unpaused_active_coverage_wins_over_longer_paused_anomaly()
    {
        var gymDate = new DateOnly(2026, 7, 12);
        var active = Subscription(1, 1, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 20), GymMembershipPolicy.Active);
        var paused = Subscription(2, 2, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 20), GymMembershipPolicy.Paused);

        var selected = GymMembershipPolicy.SelectCurrentCoverage(
            new[]
            {
                new MembershipCoverageCandidate(paused, HasOpenPause: true),
                new MembershipCoverageCandidate(active, HasOpenPause: false)
            },
            gymDate);

        Assert.Equal(AttendanceMembershipState.Active, selected.State);
        Assert.Equal(active.SubscriptionID, selected.Subscription?.SubscriptionID);
        Assert.Equal(active.PlanID, selected.PlanId);
        Assert.Equal(active.EndDate, selected.ExpiryDate);
    }

    [Fact]
    public void Closed_pause_after_resume_does_not_block_but_open_pause_does()
    {
        var gymDate = new DateOnly(2026, 7, 12);
        var active = Subscription(1, 1, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 30), GymMembershipPolicy.Active);

        var closed = GymMembershipPolicy.SelectCurrentCoverage(
            new[] { new MembershipCoverageCandidate(active, HasOpenPause: false) },
            gymDate);
        var open = GymMembershipPolicy.SelectCurrentCoverage(
            new[] { new MembershipCoverageCandidate(active, HasOpenPause: true) },
            gymDate);

        Assert.Equal(AttendanceMembershipState.Active, closed.State);
        Assert.Equal(AttendanceMembershipState.Paused, open.State);
    }

    [Fact]
    public void Status_lag_does_not_grant_coverage_after_inclusive_end_day()
    {
        var staleActive = Subscription(
            1,
            1,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 11),
            GymMembershipPolicy.Active);

        var selected = GymMembershipPolicy.SelectCurrentCoverage(
            new[] { new MembershipCoverageCandidate(staleActive, HasOpenPause: false) },
            new DateOnly(2026, 7, 12));

        Assert.Equal(AttendanceMembershipState.Inactive, selected.State);
        Assert.Null(selected.Subscription);
    }

    [Fact]
    public void Only_existing_nondeleted_status_active_member_is_mutation_eligible()
    {
        Assert.True(GymMembershipPolicy.IsActiveMember(new Member
        {
            MemberID = 1,
            Status = GymMembershipPolicy.MemberActive
        }));
        Assert.False(GymMembershipPolicy.IsActiveMember(new Member
        {
            MemberID = 2,
            Status = "Inactive"
        }));
        Assert.False(GymMembershipPolicy.IsActiveMember(new Member
        {
            MemberID = 3,
            Status = GymMembershipPolicy.MemberActive,
            IsDeleted = true
        }));
        Assert.False(GymMembershipPolicy.IsActiveMember(null));
    }

    public static IEnumerable<object[]> InvalidCalendarInputs()
    {
        yield return new object[] { default(DateTime) };
        yield return new object[] { new DateTime(2026, 7, 1, 0, 0, 1) };
        yield return new object[] { new DateTime(2026, 7, 1, 12, 0, 0) };
    }

    private static Subscription Subscription(
        int id,
        int planId,
        DateOnly start,
        DateOnly end,
        string status)
    {
        return new Subscription
        {
            SubscriptionID = id,
            MemberID = 1,
            PlanID = planId,
            StartDate = GymMembershipPolicy.ToStorageDate(start),
            EndDate = GymMembershipPolicy.ToStorageDate(end),
            Status = status
        };
    }
}
