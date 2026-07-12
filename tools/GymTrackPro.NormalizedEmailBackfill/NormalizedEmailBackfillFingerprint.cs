using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GymTrackPro.NormalizedEmailBackfill;

public static class NormalizedEmailBackfillFingerprint
{
    public const string AlgorithmVersion = "GTP-NORMALIZED-EMAIL-SNAPSHOT-V1";
    public const int HexLength = 64;

    private static readonly byte[] RowDomain =
        Encoding.ASCII.GetBytes(AlgorithmVersion + ":ROW\0");
    private static readonly byte[] SnapshotDomain =
        Encoding.ASCII.GetBytes(AlgorithmVersion + ":SNAPSHOT\0");

    public static bool TryNormalize(string? value, out string normalizedFingerprint)
    {
        normalizedFingerprint = string.Empty;
        if (value is null
            || value.Length != HexLength
            || value.Any(character => !IsHexCharacter(character)))
        {
            return false;
        }

        normalizedFingerprint = value.ToUpperInvariant();
        return true;
    }

    private static bool IsHexCharacter(char value) =>
        (value >= '0' && value <= '9')
        || (value >= 'a' && value <= 'f')
        || (value >= 'A' && value <= 'F');

    internal static string ComputeRowState(
        int userId,
        string? email,
        string? normalizedEmail,
        string? derivedNormalizedEmail)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(RowDomain);
        AppendInt32(hash, userId);
        AppendField(hash, email);
        AppendField(hash, normalizedEmail);
        AppendField(hash, derivedNormalizedEmail);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string ComputeSnapshot(
        IEnumerable<NormalizedEmailBackfillExpectedRow> rows,
        bool usePostState)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(SnapshotDomain);
        foreach (var row in rows)
        {
            AppendInt32(hash, row.UserId);
            hash.AppendData(Convert.FromHexString(
                usePostState
                    ? row.PostStateFingerprint
                    : row.PreStateFingerprint));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendField(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            hash.AppendData(new byte[] { 0 });
            return;
        }

        hash.AppendData(new byte[] { 1 });
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
