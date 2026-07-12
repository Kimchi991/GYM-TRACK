using System.Security.Cryptography;
using System.Text;

namespace GymTrackPro.API.Authentication;

public static class InviteCodeCodec
{
    public const int EntropyBytes = 32;
    public const int EncodedLength = 43;
    public const int HashBytes = 32;
    private const string CanonicalFinalCharacters = "AEIMQUYcgkosw048";

    public static string Generate()
    {
        Span<byte> entropy = stackalloc byte[EntropyBytes];
        RandomNumberGenerator.Fill(entropy);
        return Convert.ToBase64String(entropy)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryHash(string? inviteCode, out byte[] hash)
    {
        hash = Array.Empty<byte>();
        if (!IsValid(inviteCode))
        {
            return false;
        }

        hash = SHA256.HashData(Encoding.ASCII.GetBytes(inviteCode!));
        return hash.Length == HashBytes;
    }

    public static bool IsValid(string? inviteCode)
    {
        if (inviteCode is null || inviteCode.Length != EncodedLength)
        {
            return false;
        }

        foreach (var character in inviteCode)
        {
            if ((character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character is '-' or '_')
            {
                continue;
            }

            return false;
        }

        return CanonicalFinalCharacters.IndexOf(inviteCode[^1]) >= 0;
    }

    public static bool IsValidUtf8(ReadOnlySpan<byte> inviteCode)
    {
        if (inviteCode.Length != EncodedLength)
        {
            return false;
        }

        foreach (var character in inviteCode)
        {
            if ((character >= (byte)'A' && character <= (byte)'Z')
                || (character >= (byte)'a' && character <= (byte)'z')
                || (character >= (byte)'0' && character <= (byte)'9')
                || character is (byte)'-' or (byte)'_')
            {
                continue;
            }

            return false;
        }

        return CanonicalFinalCharacters.IndexOf((char)inviteCode[^1]) >= 0;
    }
}
