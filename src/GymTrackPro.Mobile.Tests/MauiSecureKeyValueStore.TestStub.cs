namespace GymTrackPro.Mobile.Services;

// The production adapter is platform-specific. Tests always inject an in-memory store;
// this stub only satisfies the repository's parameterless-constructor type reference.
internal sealed class MauiSecureKeyValueStore : ISecureKeyValueStore
{
    public string? Get(string key) => throw new NotSupportedException();
    public void Set(string key, string value) => throw new NotSupportedException();
    public bool Remove(string key) => throw new NotSupportedException();
}
