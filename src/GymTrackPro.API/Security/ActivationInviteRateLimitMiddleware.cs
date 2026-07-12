using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GymTrackPro.API.Authentication;

namespace GymTrackPro.API.Security;

public sealed class ActivationInviteHashShardLimiter
{
    public const int DefaultShardCount = 64;
    public const int DefaultShardPermitLimit = 10;
    public const int DefaultInvalidPermitLimit = 5;

    private readonly CounterState[] _shards;
    private readonly CounterState _invalid = new();
    private readonly int _shardPermitLimit;
    private readonly int _invalidPermitLimit;
    private readonly long _windowTicks;
    private readonly TimeProvider _timeProvider;

    public ActivationInviteHashShardLimiter()
        : this(
            DefaultShardCount,
            DefaultShardPermitLimit,
            DefaultInvalidPermitLimit,
            TimeSpan.FromMinutes(1),
            TimeProvider.System)
    {
    }

    public ActivationInviteHashShardLimiter(
        int shardCount,
        int shardPermitLimit,
        int invalidPermitLimit,
        TimeSpan window,
        TimeProvider timeProvider)
    {
        if (shardCount is < 1 or > 1024
            || shardPermitLimit < 1
            || invalidPermitLimit < 1
            || window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shardCount));
        }

        _shards = Enumerable.Range(0, shardCount)
            .Select(_ => new CounterState())
            .ToArray();
        _shardPermitLimit = shardPermitLimit;
        _invalidPermitLimit = invalidPermitLimit;
        _windowTicks = window.Ticks;
        _timeProvider = timeProvider;
    }

    public int ShardCount => _shards.Length;
    public int PartitionCount => _shards.Length + 1;

    public int GetShard(ReadOnlySpan<byte> canonicalInviteCodeUtf8)
    {
        if (!InviteCodeCodec.IsValidUtf8(canonicalInviteCodeUtf8))
        {
            throw new ArgumentException("Invite-code shape is invalid.", nameof(canonicalInviteCodeUtf8));
        }

        Span<byte> digest = stackalloc byte[InviteCodeCodec.HashBytes];
        SHA256.HashData(canonicalInviteCodeUtf8, digest);
        try
        {
            return (int)(BinaryPrimitives.ReadUInt32BigEndian(digest) % (uint)_shards.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    public bool TryAcquireShard(int shard)
    {
        if ((uint)shard >= (uint)_shards.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(shard));
        }

        return TryAcquire(_shards[shard], _shardPermitLimit);
    }

    public bool TryAcquireInvalid() => TryAcquire(_invalid, _invalidPermitLimit);

    private bool TryAcquire(CounterState state, int permitLimit)
    {
        var windowId = _timeProvider.GetUtcNow().UtcTicks / _windowTicks;
        lock (state.Gate)
        {
            if (state.WindowId != windowId)
            {
                state.WindowId = windowId;
                state.Count = 0;
            }

            if (state.Count >= permitLimit)
            {
                return false;
            }

            state.Count++;
            return true;
        }
    }

    private sealed class CounterState
    {
        public object Gate { get; } = new();
        public long WindowId { get; set; } = long.MinValue;
        public int Count { get; set; }
    }
}

public sealed class ActivationInviteRateLimitMiddleware
{
    public const string ExactPath = "/api/v1/auth/activate";
    public const int MaximumBodyBytes = 1024;

    private const string InvalidResponse =
        "{\"success\":false,\"message\":\"The activation request is invalid.\",\"errorCode\":\"INVITE_INVALID\"}";
    private readonly RequestDelegate _next;
    private readonly ActivationInviteHashShardLimiter _limiter;
    private readonly ILogger<ActivationInviteRateLimitMiddleware> _logger;

    public ActivationInviteRateLimitMiddleware(
        RequestDelegate next,
        ActivationInviteHashShardLimiter limiter,
        ILogger<ActivationInviteRateLimitMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsActivationRequest(context.Request))
        {
            await _next(context);
            return;
        }

        // Authentication and the endpoint Activation limiter run before this middleware.
        // Failed/anonymous bearer requests continue to authorization without ever reading
        // the invite body or consuming invalid/hash-shard capacity.
        if (context.User.Identity?.IsAuthenticated != true
            || !FirebaseClaimTypes.TryGetVerifiedIdentity(context.User, out _, out _))
        {
            await _next(context);
            return;
        }

        var inspection = await InspectBodyAsync(context.Request, context.RequestAborted);
        if (!inspection.IsValid)
        {
            if (!_limiter.TryAcquireInvalid())
            {
                LogRejection(context, "INVALID_BODY_BUCKET_LIMIT");
                await WriteResponseAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    RateLimitResponsePayload.Json);
                return;
            }

            await WriteResponseAsync(
                context,
                StatusCodes.Status400BadRequest,
                InvalidResponse);
            return;
        }

        if (!_limiter.TryAcquireShard(inspection.Shard))
        {
            LogRejection(context, "INVITE_HASH_SHARD_LIMIT");
            await WriteResponseAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                RateLimitResponsePayload.Json);
            return;
        }

        await _next(context);
    }

    private void LogRejection(HttpContext context, string reasonCategory) =>
        _logger.LogWarning(
            "Activation admission rejected. ReasonCategory: {ReasonCategory}; CorrelationId: {CorrelationId}",
            reasonCategory,
            context.TraceIdentifier);

    public static bool IsActivationRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        var path = request.Path.Value;
        return string.Equals(path, ExactPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, $"{ExactPath}/", StringComparison.OrdinalIgnoreCase);
    }

    private InspectionResult Inspect(ReadOnlySpan<byte> body)
    {
        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var rootStarted = false;
            var rootEnded = false;
            var foundInviteCode = false;
            var shard = 0;

            while (reader.Read())
            {
                if (!rootStarted)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        return default;
                    }

                    rootStarted = true;
                    continue;
                }

                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                {
                    rootEnded = true;
                    continue;
                }

                if (reader.TokenType != JsonTokenType.PropertyName
                    || reader.CurrentDepth != 1)
                {
                    continue;
                }

                var propertyName = reader.GetString();
                if (!string.Equals(propertyName, "inviteCode", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (foundInviteCode
                    || !reader.Read()
                    || reader.TokenType != JsonTokenType.String)
                {
                    return default;
                }

                var inviteCode = reader.GetString();
                if (!InviteCodeCodec.IsValid(inviteCode))
                {
                    return default;
                }

                var canonicalCode = new byte[InviteCodeCodec.EncodedLength];
                try
                {
                    var written = Encoding.ASCII.GetBytes(inviteCode, canonicalCode.AsSpan());
                    if (written != canonicalCode.Length)
                    {
                        return default;
                    }

                    shard = _limiter.GetShard(canonicalCode);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(canonicalCode);
                }
                foundInviteCode = true;
            }

            return rootStarted && rootEnded && foundInviteCode
                ? new InspectionResult(true, shard)
                : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private async Task<InspectionResult> InspectBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaximumBodyBytes)
        {
            return default;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumBodyBytes + 1);
        var bytesRead = 0;
        try
        {
            request.EnableBuffering(
                bufferThreshold: MaximumBodyBytes + 1,
                bufferLimit: MaximumBodyBytes + 1);
            request.Body.Position = 0;
            while (bytesRead < MaximumBodyBytes + 1)
            {
                var read = await request.Body.ReadAsync(
                    buffer.AsMemory(bytesRead, MaximumBodyBytes + 1 - bytesRead),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            return bytesRead is > 0 and <= MaximumBodyBytes
                ? Inspect(buffer.AsSpan(0, bytesRead))
                : default;
        }
        catch (IOException)
        {
            return default;
        }
        finally
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, bytesRead));
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static async Task WriteResponseAsync(
        HttpContext context,
        int statusCode,
        string response)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            context.Response.Headers.RetryAfter = "60";
        }
        await context.Response.WriteAsync(response, context.RequestAborted);
    }

    private readonly record struct InspectionResult(bool IsValid, int Shard);
}
