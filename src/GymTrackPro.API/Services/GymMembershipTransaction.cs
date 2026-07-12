using System.Data;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymTrackPro.API.Services;

internal static class GymMembershipTransaction
{
    public static async Task<TResult> ExecuteVerifiedAsync<TKey, TResult>(
        GymDbContext context,
        TKey verificationKey,
        Func<TKey, CancellationToken, Task<TResult>> operation,
        Func<TKey, CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        if (!context.Database.IsRelational())
        {
            context.ChangeTracker.Clear();
            try
            {
                var result = await operation(verificationKey, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                verificationKey,
                async (key, transactionToken) =>
                {
                    context.ChangeTracker.Clear();
                    var result = await operation(key, transactionToken);
                    await context.SaveChangesAsync(transactionToken);
                    return result;
                },
                async (key, verificationToken) =>
                {
                    context.ChangeTracker.Clear();
                    return await verifySucceeded(key, verificationToken);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public static Task<Member?> LockMemberAsync(
        GymDbContext context,
        int memberId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(context))
        {
            return context.Members
                .FromSqlInterpolated(
                    $"SELECT TOP (1) * FROM [Members] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return context.Members.SingleOrDefaultAsync(
            member => member.MemberID == memberId,
            cancellationToken);
    }

    public static Task<List<Subscription>> LockMemberSubscriptionsAsync(
        GymDbContext context,
        int memberId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(context))
        {
            return context.Subscriptions
                .FromSqlInterpolated(
                    $"SELECT * FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
                .ToListAsync(cancellationToken);
        }

        return context.Subscriptions
            .Where(subscription => subscription.MemberID == memberId)
            .ToListAsync(cancellationToken);
    }

    public static Task<Subscription?> LockSubscriptionAsync(
        GymDbContext context,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(context))
        {
            return context.Subscriptions
                .FromSqlInterpolated(
                    $"SELECT TOP (1) * FROM [Subscriptions] WITH (UPDLOCK, HOLDLOCK) WHERE [SubscriptionID] = {subscriptionId}")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return context.Subscriptions.SingleOrDefaultAsync(
            subscription => subscription.SubscriptionID == subscriptionId,
            cancellationToken);
    }

    public static Task<Payment?> LockPaymentAsync(
        GymDbContext context,
        int paymentId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(context))
        {
            return context.Payments
                .FromSqlInterpolated(
                    $"SELECT TOP (1) * FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [PaymentID] = {paymentId}")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return context.Payments.SingleOrDefaultAsync(
            payment => payment.PaymentID == paymentId,
            cancellationToken);
    }

    public static Task<List<Payment>> LockMemberPaymentsAsync(
        GymDbContext context,
        int memberId,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(context))
        {
            return context.Payments
                .FromSqlInterpolated(
                    $"SELECT * FROM [Payments] WITH (UPDLOCK, HOLDLOCK) WHERE [MemberID] = {memberId}")
                .ToListAsync(cancellationToken);
        }

        return context.Payments
            .Where(payment => payment.MemberID == memberId)
            .ToListAsync(cancellationToken);
    }

    private static bool IsSqlServer(GymDbContext context)
    {
        return string.Equals(
            context.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.SqlServer",
            StringComparison.Ordinal);
    }
}
