namespace GymTrackPro.Mobile.Services;

/// <summary>
/// Clears account-scoped local data before the persisted Firebase session is removed.
/// Cache implementations are supplied separately so authentication does not depend on
/// SQLite or a particular offline-storage design.
/// </summary>
public interface IAccountLocalDataCleaner
{
    Task ClearAccountDataAsync(
        string firebaseUid,
        CancellationToken cancellationToken = default);
}

public sealed record AppLogoutResult(
    bool AccountDataCleanerRegistered,
    bool AccountDataCleared);

/// <summary>
/// Coordinates account-scoped cleanup with removal of the authentication session.
/// The supplied sign-out callback is deliberately low-level so terminal refresh
/// handling can use this boundary while already holding the Firebase session gate.
/// </summary>
public interface IAccountSessionInvalidator
{
    Task<AppLogoutResult> InvalidateAsync(
        string? firebaseUid,
        Func<Task> signOutAsync,
        CancellationToken cancellationToken = default);
}

public sealed class AccountSessionInvalidator : IAccountSessionInvalidator
{
    private readonly IReadOnlyList<IAccountLocalDataCleaner> _accountDataCleaners;

    public AccountSessionInvalidator(
        IEnumerable<IAccountLocalDataCleaner> accountDataCleaners)
    {
        _accountDataCleaners = accountDataCleaners?.ToArray()
            ?? throw new ArgumentNullException(nameof(accountDataCleaners));
    }

    public async Task<AppLogoutResult> InvalidateAsync(
        string? firebaseUid,
        Func<Task> signOutAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signOutAsync);

        var cleanerRegistered = _accountDataCleaners.Count > 0;
        var accountDataCleared = cleanerRegistered && !string.IsNullOrWhiteSpace(firebaseUid);

        try
        {
            if (!string.IsNullOrWhiteSpace(firebaseUid))
            {
                foreach (var cleaner in _accountDataCleaners)
                {
                    try
                    {
                        await cleaner
                            .ClearAccountDataAsync(firebaseUid, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // A cache cleanup failure must never preserve a revoked or
                        // explicitly terminated Firebase refresh session.
                        accountDataCleared = false;
                    }
                }
            }
        }
        finally
        {
            await signOutAsync().ConfigureAwait(false);
        }

        return new AppLogoutResult(cleanerRegistered, accountDataCleared);
    }
}

public interface IAppLogoutService
{
    Task<AppLogoutResult> LogoutAsync(CancellationToken cancellationToken = default);
}

public sealed class AppLogoutService : IAppLogoutService
{
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IAccountSessionInvalidator _sessionInvalidator;

    public AppLogoutService(
        IFirebaseAuthService firebaseAuthService,
        IAccountSessionInvalidator sessionInvalidator)
    {
        _firebaseAuthService = firebaseAuthService
            ?? throw new ArgumentNullException(nameof(firebaseAuthService));
        _sessionInvalidator = sessionInvalidator
            ?? throw new ArgumentNullException(nameof(sessionInvalidator));
    }

    public async Task<AppLogoutResult> LogoutAsync(
        CancellationToken cancellationToken = default)
    {
        return await _sessionInvalidator
            .InvalidateAsync(
                _firebaseAuthService.CurrentUserId,
                () => _firebaseAuthService.SignOutAsync(CancellationToken.None),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
