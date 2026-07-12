using System;
using System.Text.Json;
using Firebase.Auth;
using Firebase.Auth.Repository;

namespace GymTrackPro.Mobile.Services;

/// <summary>
/// Synchronous key/value boundary required by FirebaseAuthentication.net's
/// synchronous IUserRepository contract.
/// </summary>
public interface ISecureKeyValueStore
{
    string? Get(string key);
    void Set(string key, string value);
    bool Remove(string key);
}

/// <summary>
/// Persists the Firebase refresh credential in platform SecureStorage. The key is
/// app-, Firebase-project-, environment-, and schema-scoped.
/// </summary>
public sealed class SecureStorageUserRepository : IUserRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private readonly object _sync = new();
    private readonly ISecureKeyValueStore _store;
    private readonly string _storageKey;

    public SecureStorageUserRepository()
        : this(new MauiSecureKeyValueStore(), FirebaseAuthSettings.UserStorageKey)
    {
    }

    public SecureStorageUserRepository(ISecureKeyValueStore store, string storageKey)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _storageKey = string.IsNullOrWhiteSpace(storageKey)
            ? throw new ArgumentException("A namespaced storage key is required.", nameof(storageKey))
            : storageKey;
    }

    public bool UserExists()
    {
        lock (_sync)
        {
            return !string.IsNullOrWhiteSpace(_store.Get(_storageKey));
        }
    }

    public (UserInfo, FirebaseCredential) ReadUser()
    {
        lock (_sync)
        {
            var data = _store.Get(_storageKey);
            if (string.IsNullOrWhiteSpace(data))
            {
                return (null!, null!);
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<StoredFirebaseUser>(data, SerializerOptions);
                if (envelope?.Info is null || envelope.Credential is null ||
                    string.IsNullOrWhiteSpace(envelope.Info.Uid) ||
                    string.IsNullOrWhiteSpace(envelope.Credential.RefreshToken))
                {
                    _store.Remove(_storageKey);
                    return (null!, null!);
                }

                return (envelope.Info, envelope.Credential);
            }
            catch (JsonException)
            {
                // A malformed or incompatible credential must fail closed instead of
                // leaving FirebaseAuthClient in a partially authenticated state.
                _store.Remove(_storageKey);
                return (null!, null!);
            }
        }
    }

    public void SaveUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Uid) ||
            string.IsNullOrWhiteSpace(user.Credential?.RefreshToken))
        {
            throw new InvalidOperationException("Firebase returned an incomplete refresh session.");
        }

        var envelope = new StoredFirebaseUser
        {
            Info = user.Info,
            Credential = user.Credential
        };

        var data = JsonSerializer.Serialize(envelope, SerializerOptions);
        lock (_sync)
        {
            _store.Set(_storageKey, data);
        }
    }

    public void DeleteUser()
    {
        lock (_sync)
        {
            _store.Remove(_storageKey);
        }
    }

    private sealed class StoredFirebaseUser
    {
        public UserInfo? Info { get; init; }
        public FirebaseCredential? Credential { get; init; }
    }
}
