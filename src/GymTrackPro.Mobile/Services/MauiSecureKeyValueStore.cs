using Microsoft.Maui.Storage;

namespace GymTrackPro.Mobile.Services;

internal sealed class MauiSecureKeyValueStore : ISecureKeyValueStore
{
    public string? Get(string key) =>
        SecureStorage.Default.GetAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();

    public void Set(string key, string value) =>
        SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false).GetAwaiter().GetResult();

    public bool Remove(string key) => SecureStorage.Default.Remove(key);
}
