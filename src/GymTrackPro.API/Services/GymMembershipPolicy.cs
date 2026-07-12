using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Constants;
using Microsoft.AspNetCore.Http;

namespace GymTrackPro.API.Services;

/// <summary>
/// Canonical membership calendar and status policy. Subscription StartDate/EndDate
/// are inclusive gym-calendar dates stored as midnight DateTimeKind.Unspecified.
/// </summary>
public static class GymMembershipPolicy
{
    public const string MemberActive = "Active";
    public const string PlanActive = "Active";
    public const string PendingPayment = "PendingPayment";
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";

    public static DateTime NormalizeCalendarDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    }

    public static DateTime RequireCalendarInput(DateTime value, string parameterName)
    {
        if (value == default || value.TimeOfDay != TimeSpan.Zero)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.MembershipDateInvalid,
                $"{parameterName} must be a non-default gym calendar date at midnight.");
        }

        return NormalizeCalendarDate(value);
    }

    public static DateTime ToStorageDate(DateOnly value)
    {
        return DateTime.SpecifyKind(
            value.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
    }

    public static DateOnly ToCalendarDate(DateTime value)
    {
        return DateOnly.FromDateTime(value);
    }

    public static DateTime CalculateInclusiveEnd(DateTime startDate, int durationDays)
    {
        if (durationDays < 1)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.MembershipDateInvalid,
                "Membership duration must contain at least one day.");
        }

        var normalizedStart = NormalizeCalendarDate(startDate);
        try
        {
            return normalizedStart.AddDays(durationDays - 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.MembershipDateInvalid,
                "Membership duration exceeds the supported calendar range.");
        }
    }

    public static bool Covers(Subscription subscription, DateOnly gymDate)
    {
        return ToCalendarDate(subscription.StartDate) <= gymDate
            && ToCalendarDate(subscription.EndDate) >= gymDate;
    }

    public static bool Overlaps(
        DateTime firstStart,
        DateTime firstEnd,
        DateTime secondStart,
        DateTime secondEnd)
    {
        var firstStartDate = ToCalendarDate(firstStart);
        var firstEndDate = ToCalendarDate(firstEnd);
        var secondStartDate = ToCalendarDate(secondStart);
        var secondEndDate = ToCalendarDate(secondEnd);
        return firstStartDate <= secondEndDate && secondStartDate <= firstEndDate;
    }

    public static bool IsBlockingStatus(string? status)
    {
        return string.Equals(status, PendingPayment, StringComparison.Ordinal)
            || string.Equals(status, Active, StringComparison.Ordinal)
            || string.Equals(status, Paused, StringComparison.Ordinal);
    }

    public static bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, Expired, StringComparison.Ordinal)
            || string.Equals(status, Cancelled, StringComparison.Ordinal);
    }

    public static bool IsActiveMember(Member? member)
    {
        return member is not null
            && !member.IsDeleted
            && string.Equals(member.Status, MemberActive, StringComparison.Ordinal);
    }

    public static MembershipCoverageSelection SelectCurrentCoverage(
        IEnumerable<MembershipCoverageCandidate> candidates,
        DateOnly gymDate)
    {
        var covering = candidates
            .Where(candidate => Covers(candidate.Subscription, gymDate))
            .ToArray();
        var active = OrderDeterministically(covering.Where(candidate =>
                string.Equals(candidate.Subscription.Status, Active, StringComparison.Ordinal)
                && !candidate.HasOpenPause))
            .FirstOrDefault();
        if (active is not null)
        {
            return new MembershipCoverageSelection(
                AttendanceMembershipState.Active,
                active.Subscription);
        }

        var paused = OrderDeterministically(covering.Where(candidate =>
                string.Equals(candidate.Subscription.Status, Paused, StringComparison.Ordinal)
                || (string.Equals(candidate.Subscription.Status, Active, StringComparison.Ordinal)
                    && candidate.HasOpenPause)))
            .FirstOrDefault();
        return paused is null
            ? new MembershipCoverageSelection(AttendanceMembershipState.Inactive, null)
            : new MembershipCoverageSelection(
                AttendanceMembershipState.Paused,
                paused.Subscription);
    }

    public static Subscription? SelectHistoricalCoverage(
        IEnumerable<MembershipCoverageCandidate> candidates,
        DateOnly gymDate)
    {
        return candidates
            .Where(candidate => Covers(candidate.Subscription, gymDate))
            .OrderBy(candidate => HistoricalStatusRank(candidate.Subscription.Status, candidate.HasOpenPause))
            .ThenByDescending(candidate => ToCalendarDate(candidate.Subscription.EndDate))
            .ThenByDescending(candidate => ToCalendarDate(candidate.Subscription.StartDate))
            .ThenByDescending(candidate => candidate.Subscription.SubscriptionID)
            .Select(candidate => candidate.Subscription)
            .FirstOrDefault();
    }

    private static IOrderedEnumerable<MembershipCoverageCandidate> OrderDeterministically(
        IEnumerable<MembershipCoverageCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => ToCalendarDate(candidate.Subscription.EndDate))
            .ThenByDescending(candidate => ToCalendarDate(candidate.Subscription.StartDate))
            .ThenByDescending(candidate => candidate.Subscription.SubscriptionID);
    }

    private static int HistoricalStatusRank(string? status, bool hasOpenPause)
    {
        if (string.Equals(status, Active, StringComparison.Ordinal) && !hasOpenPause)
        {
            return 0;
        }

        if (string.Equals(status, Paused, StringComparison.Ordinal)
            || (string.Equals(status, Active, StringComparison.Ordinal) && hasOpenPause))
        {
            return 1;
        }

        return string.Equals(status, PendingPayment, StringComparison.Ordinal) ? 2 : 3;
    }
}

public sealed record MembershipCoverageCandidate(
    Subscription Subscription,
    bool HasOpenPause);

public sealed record MembershipCoverageSelection(
    AttendanceMembershipState State,
    Subscription? Subscription)
{
    public DateTime? ExpiryDate => Subscription?.EndDate;
    public int? PlanId => Subscription?.PlanID;
}
