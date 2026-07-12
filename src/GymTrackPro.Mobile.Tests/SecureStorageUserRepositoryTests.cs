using System.Text.Json;
using Firebase.Auth;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.Tests;

public sealed class SecureStorageUserRepositoryTests
{
    private const string TestKey = "com.companyname.gymtrackpro.mobile.auth.firebase.test.v1.user";

    [Fact]
    public void Firebase_storage_keys_are_app_project_environment_and_schema_scoped()
    {
        var developmentKey = FirebaseAuthSettings.BuildUserStorageKey("development");
        var productionKey = FirebaseAuthSettings.BuildUserStorageKey("production");

        Assert.Equal(
            "com.companyname.gymtrackpro.mobile.auth.firebase.fithub-cf45f.development.v1.user",
            developmentKey);
        Assert.Equal(
            "com.companyname.gymtrackpro.mobile.auth.firebase.fithub-cf45f.production.v1.user",
            productionKey);
        Assert.NotEqual(developmentKey, productionKey);
        Assert.Equal(
            FirebaseAuthSettings.BuildUserStorageKey(FirebaseAuthSettings.EnvironmentName),
            FirebaseAuthSettings.UserStorageKey);
        Assert.Equal(
            ["auth_token", "firebase_token", "firebase_user_v1"],
            FirebaseAuthSettings.LegacyStorageKeys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Production")]
    [InlineData("staging")]
    [InlineData("production/other")]
    public void Firebase_storage_key_rejects_unapproved_environment_names(string environmentName)
    {
        Assert.Throws<ArgumentException>(() =>
            FirebaseAuthSettings.BuildUserStorageKey(environmentName));
    }

    [Fact]
    public void Reads_persisted_refresh_session()
    {
        var store = new InMemorySecureStore();
        store.Set(TestKey, JsonSerializer.Serialize(new
        {
            Info = new UserInfo
            {
                Uid = "firebase-uid",
                Email = "member@example.com",
                IsEmailVerified = true
            },
            Credential = new FirebaseCredential
            {
                IdToken = "id-token",
                RefreshToken = "rotated-refresh-token",
                Created = DateTime.UtcNow,
                ExpiresIn = 3600
            }
        }));
        var repository = new SecureStorageUserRepository(store, TestKey);

        var (info, credential) = repository.ReadUser();

        Assert.Equal("firebase-uid", info.Uid);
        Assert.Equal("rotated-refresh-token", credential.RefreshToken);
        Assert.True(repository.UserExists());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void Corrupt_or_incomplete_session_is_deleted_and_fails_closed(string value)
    {
        var store = new InMemorySecureStore();
        store.Set(TestKey, value);
        var repository = new SecureStorageUserRepository(store, TestKey);

        var (info, credential) = repository.ReadUser();

        Assert.Null(info);
        Assert.Null(credential);
        Assert.False(repository.UserExists());
    }

    private sealed class InMemorySecureStore : ISecureKeyValueStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public void Set(string key, string value) => _values[key] = value;

        public bool Remove(string key) => _values.Remove(key);
    }
}
