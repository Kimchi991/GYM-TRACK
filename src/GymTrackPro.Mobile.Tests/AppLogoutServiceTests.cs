using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.Tests;

public sealed class AppLogoutServiceTests
{
    [Fact]
    public async Task Account_cleaner_runs_for_current_uid_before_firebase_sign_out()
    {
        var calls = new List<string>();
        var auth = new FakeFirebaseAuthService("firebase-user", calls);
        var cleaner = new RecordingCleaner(calls);
        var service = new AppLogoutService(
            auth,
            new AccountSessionInvalidator([cleaner]));

        var result = await service.LogoutAsync();

        Assert.True(result.AccountDataCleanerRegistered);
        Assert.True(result.AccountDataCleared);
        Assert.Equal(["clean:firebase-user", "signout"], calls);
        Assert.Null(auth.CurrentUserId);
    }

    [Fact]
    public async Task Cleaner_failure_is_reported_but_cannot_preserve_firebase_session()
    {
        var calls = new List<string>();
        var auth = new FakeFirebaseAuthService("firebase-user", calls);
        var service = new AppLogoutService(
            auth,
            new AccountSessionInvalidator([new ThrowingCleaner(calls)]));

        var result = await service.LogoutAsync();

        Assert.True(result.AccountDataCleanerRegistered);
        Assert.False(result.AccountDataCleared);
        Assert.Equal(["clean:firebase-user", "signout"], calls);
        Assert.Null(auth.CurrentUserId);
    }

    [Fact]
    public async Task Missing_cleaner_is_disclosed_and_session_is_still_removed()
    {
        var calls = new List<string>();
        var auth = new FakeFirebaseAuthService("firebase-user", calls);
        var service = new AppLogoutService(
            auth,
            new AccountSessionInvalidator([]));

        var result = await service.LogoutAsync();

        Assert.False(result.AccountDataCleanerRegistered);
        Assert.False(result.AccountDataCleared);
        Assert.Equal(["signout"], calls);
    }

    [Fact]
    public async Task Terminal_refresh_invalidation_clears_captured_uid_before_sign_out()
    {
        var calls = new List<string>();
        var cleaner = new RecordingCleaner(calls);
        var invalidator = new AccountSessionInvalidator([cleaner]);
        string? currentUid = "revoked-firebase-user";
        var capturedUid = currentUid;

        var result = await invalidator.InvalidateAsync(
            capturedUid,
            () =>
            {
                calls.Add("signout");
                currentUid = null;
                return Task.CompletedTask;
            });

        Assert.True(result.AccountDataCleared);
        Assert.Equal(["clean:revoked-firebase-user", "signout"], calls);
        Assert.Null(currentUid);
    }

    [Fact]
    public async Task Terminal_refresh_cleanup_failure_cannot_preserve_revoked_session()
    {
        var calls = new List<string>();
        var invalidator = new AccountSessionInvalidator([new ThrowingCleaner(calls)]);
        var sessionPresent = true;

        var result = await invalidator.InvalidateAsync(
            "revoked-firebase-user",
            () =>
            {
                calls.Add("signout");
                sessionPresent = false;
                return Task.CompletedTask;
            });

        Assert.False(result.AccountDataCleared);
        Assert.False(sessionPresent);
        Assert.Equal(["clean:revoked-firebase-user", "signout"], calls);
    }

    private sealed class RecordingCleaner(List<string> calls) : IAccountLocalDataCleaner
    {
        public Task ClearAccountDataAsync(
            string firebaseUid,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"clean:{firebaseUid}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCleaner(List<string> calls) : IAccountLocalDataCleaner
    {
        public Task ClearAccountDataAsync(
            string firebaseUid,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"clean:{firebaseUid}");
            throw new InvalidOperationException("Simulated cache failure.");
        }
    }

    private sealed class FakeFirebaseAuthService(
        string? currentUserId,
        List<string> calls) : IFirebaseAuthService
    {
        public string? CurrentUserId { get; private set; } = currentUserId;

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("signout");
            CurrentUserId = null;
            return Task.CompletedTask;
        }

        public Task LogoutAsync() => SignOutAsync();
        public Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> HasSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentUserId is not null);
        public Task<string> LoginAsync(string email, string password) => throw new NotSupportedException();
        public Task<string> RegisterAsync(string email, string password) => throw new NotSupportedException();
        public Task ResetPasswordAsync(string email) => throw new NotSupportedException();
        public Task<string> LoginWithGoogleAsync(string oauthToken) => throw new NotSupportedException();
        public Task SendEmailVerificationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsEmailVerifiedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetFreshTokenAsync() => Task.FromResult<string?>(null);
        public Task<bool> HasValidSessionAsync() => Task.FromResult(CurrentUserId is not null);
        public string? GetCurrentUid() => CurrentUserId;
    }
}
