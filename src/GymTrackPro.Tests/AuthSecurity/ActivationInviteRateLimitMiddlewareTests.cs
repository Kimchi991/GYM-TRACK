using System.Text;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Controllers;
using GymTrackPro.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class ActivationInviteRateLimitMiddlewareTests
{
    [Fact]
    public async Task Exact_activation_route_is_inspected_and_valid_body_is_rewound_for_model_binding()
    {
        var code = InviteCodeCodec.Generate();
        var body = $"{{\"inviteCode\":\"{code}\",\"operationId\":\"{Guid.NewGuid():D}\"}}";
        var context = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
        string? downstreamBody = null;
        var middleware = CreateMiddleware(async nextContext =>
        {
            using var reader = new StreamReader(
                nextContext.Request.Body,
                Encoding.UTF8,
                leaveOpen: true);
            downstreamBody = await reader.ReadToEndAsync();
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(body, downstreamBody);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Json_interpretation_matches_case_insensitive_binding_and_ignores_nested_decoys()
    {
        var code = InviteCodeCodec.Generate();
        var decoy = InviteCodeCodec.Generate();
        var escapedValue = $"\\u{(int)code[0]:X4}{code[1..]}";
        var bodies = new[]
        {
            $"{{\"INVITECODE\":\"{code}\"}}",
            $"{{\"invite\\u0043ode\":\"{escapedValue}\"}}",
            $"{{\"wrapper\":{{\"inviteCode\":\"{decoy}\"}},\"InviteCode\":\"{code}\"}}"
        };
        var calls = 0;
        var middleware = CreateMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        foreach (var body in bodies)
        {
            var context = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal(0, context.Request.Body.Position);
        }

        Assert.Equal(bodies.Length, calls);
    }

    [Fact]
    public async Task Duplicate_effective_invite_names_are_rejected_in_every_order()
    {
        var firstCode = InviteCodeCodec.Generate();
        var secondCode = InviteCodeCodec.Generate();
        var bodies = new[]
        {
            $"{{\"inviteCode\":\"{firstCode}\",\"InviteCode\":\"{secondCode}\"}}",
            $"{{\"INVITECODE\":\"{secondCode}\",\"inviteCode\":\"{firstCode}\"}}",
            $"{{\"inviteCode\":\"{firstCode}\",\"invite\\u0043ode\":\"{secondCode}\"}}"
        };
        var calls = 0;
        var middleware = CreateMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        foreach (var body in bodies)
        {
            var context = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
            await middleware.InvokeAsync(context);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.Equal(0, context.Request.Body.Position);
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Trailing_json_is_rejected_and_exact_maximum_body_is_accepted()
    {
        var code = InviteCodeCodec.Generate();
        var valid = $"{{\"inviteCode\":\"{code}\"}}";
        var exactMaximum = valid.PadRight(
            ActivationInviteRateLimitMiddleware.MaximumBodyBytes,
            ' ');
        var calls = 0;
        var middleware = CreateMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        var trailing = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            $"{valid}{{}}");
        var maximum = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            exactMaximum);

        await middleware.InvokeAsync(trailing);
        await middleware.InvokeAsync(maximum);

        Assert.Equal(StatusCodes.Status400BadRequest, trailing.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, maximum.Response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal(0, maximum.Request.Body.Position);
    }

    [Fact]
    public async Task Unauthenticated_canonical_requests_never_consume_invite_shard_capacity()
    {
        var limiter = CreateLimiter(shardCount: 1, shardPermitLimit: 1);
        var downstreamCalls = 0;
        var middleware = new ActivationInviteRateLimitMiddleware(
            _ =>
            {
                downstreamCalls++;
                return Task.CompletedTask;
            },
            limiter,
            NullLogger<ActivationInviteRateLimitMiddleware>.Instance);
        var code = InviteCodeCodec.Generate();
        var body = $"{{\"inviteCode\":\"{code}\"}}";

        for (var index = 0; index < 5; index++)
        {
            var unauthenticated = CreateContext(
                ActivationInviteRateLimitMiddleware.ExactPath,
                body);
            unauthenticated.User = new ClaimsPrincipal(new ClaimsIdentity());
            await middleware.InvokeAsync(unauthenticated);
            Assert.Equal(0, unauthenticated.Request.Body.Position);
        }

        var firstAuthenticated = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            body);
        var secondAuthenticated = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            body);
        await middleware.InvokeAsync(firstAuthenticated);
        await middleware.InvokeAsync(secondAuthenticated);

        Assert.Equal(6, downstreamCalls);
        Assert.Equal(StatusCodes.Status200OK, firstAuthenticated.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, secondAuthenticated.Response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_rate_limiter_rejections_do_not_charge_authenticated_invite_shard()
    {
        var services = new ServiceCollection();
        var telemetry = new RecordingLoggerProvider();
        services.AddLogging(builder => builder.AddProvider(telemetry));
        services.AddSingleton(new ActivationInviteHashShardLimiter(
            shardCount: 1,
            shardPermitLimit: 1,
            invalidPermitLimit: 1,
            window: TimeSpan.FromHours(1),
            TimeProvider.System));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RequestRateLimitRejectionHandler.HandleAsync;
            options.AddPolicy("Activation", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Request.Headers["X-Test-Partition"].ToString(),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        application.UseRateLimiter();
        application.UseMiddleware<ActivationInviteRateLimitMiddleware>();
        var downstreamCalls = 0;
        application.Run(_ =>
        {
            downstreamCalls++;
            return Task.CompletedTask;
        });
        var pipeline = application.Build();
        var code = InviteCodeCodec.Generate();
        var body = $"{{\"inviteCode\":\"{code}\"}}";

        var warmup = CreatePipelineContext("/not-activation", body, "blocked", provider);
        await pipeline(warmup);
        DefaultHttpContext? firstEndpointRejected = null;
        for (var index = 0; index < 3; index++)
        {
            var rejected = CreatePipelineContext(
                ActivationInviteRateLimitMiddleware.ExactPath,
                body,
                "blocked",
                provider);
            await pipeline(rejected);
            firstEndpointRejected ??= rejected;
            Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        }

        var admitted = CreatePipelineContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            body,
            "admitted-1",
            provider);
        var shardRejected = CreatePipelineContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            body,
            "admitted-2",
            provider);
        await pipeline(admitted);
        await pipeline(shardRejected);

        Assert.Equal(StatusCodes.Status200OK, admitted.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, shardRejected.Response.StatusCode);
        Assert.Equal(2, downstreamCalls);
        Assert.NotNull(firstEndpointRejected);
        Assert.Equal("3600", firstEndpointRejected.Response.Headers.RetryAfter);
        Assert.Equal(RateLimitResponsePayload.Json, ReadResponse(firstEndpointRejected));
        var builtInLogs = telemetry.Entries.Where(entry =>
            entry.Contains("Request rate limit rejected", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, builtInLogs.Length);
        Assert.All(builtInLogs, log =>
        {
            Assert.Contains("Activation", log, StringComparison.Ordinal);
            Assert.DoesNotContain(code, log, StringComparison.Ordinal);
            Assert.DoesNotContain("verified-test-uid", log, StringComparison.Ordinal);
            Assert.DoesNotContain("verified@example.test", log, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(null, "Global")]
    [InlineData("Auth", "Auth")]
    [InlineData("Activation", "Activation")]
    public async Task Built_in_limiters_share_json_retry_after_and_secret_free_telemetry(
        string? policyName,
        string expectedCategory)
    {
        var services = new ServiceCollection();
        var telemetry = new RecordingLoggerProvider();
        services.AddLogging(builder => builder.AddProvider(telemetry));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RequestRateLimitRejectionHandler.HandleAsync;
            if (policyName is null)
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        "global",
                        _ => OnePermitWindow()));
            }
            else
            {
                options.AddPolicy(policyName, _ =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        policyName,
                        _ => OnePermitWindow()));
            }
        });
        using var provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        application.UseRateLimiter();
        application.Run(_ => Task.CompletedTask);
        var pipeline = application.Build();
        var first = CreateRateLimitContext(provider, policyName);
        var rejected = CreateRateLimitContext(provider, policyName);

        await pipeline(first);
        await pipeline(rejected);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.Equal("3600", rejected.Response.Headers.RetryAfter);
        Assert.Equal(RateLimitResponsePayload.Json, ReadResponse(rejected));
        var log = Assert.Single(
            telemetry.Entries,
            entry => entry.Contains("Request rate limit rejected", StringComparison.Ordinal));
        Assert.Contains(expectedCategory, log, StringComparison.Ordinal);
        Assert.Contains("rate-limit-correlation", log, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-uid", log, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive@example.test", log, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.99", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Routing_equivalent_case_and_trailing_slash_variants_cannot_bypass_inspection()
    {
        var limiter = CreateLimiter(invalidPermitLimit: 1);
        var calls = 0;
        var middleware = new ActivationInviteRateLimitMiddleware(
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
            limiter,
            NullLogger<ActivationInviteRateLimitMiddleware>.Instance);

        await middleware.InvokeAsync(CreateContext("/api/v1/auth/sync-user", "not-json"));
        var caseVariant = CreateContext("/API/v1/auth/activate", "not-json");
        await middleware.InvokeAsync(caseVariant);
        var exactInvalid = CreateContext($"{ActivationInviteRateLimitMiddleware.ExactPath}/", "not-json");
        await middleware.InvokeAsync(exactInvalid);

        Assert.Equal(1, calls);
        Assert.Equal(StatusCodes.Status400BadRequest, caseVariant.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, exactInvalid.Response.StatusCode);
    }

    [Fact]
    public async Task Oversize_body_is_rejected_generically_without_invoking_downstream()
    {
        var body = new string('x', ActivationInviteRateLimitMiddleware.MaximumBodyBytes + 1);
        var context = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
        var called = false;
        var middleware = CreateMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.DoesNotContain(body, ReadResponse(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_codes_share_one_bounded_invalid_bucket()
    {
        var limiter = CreateLimiter(invalidPermitLimit: 2);
        var logger = new RecordingLogger<ActivationInviteRateLimitMiddleware>();
        var middleware = new ActivationInviteRateLimitMiddleware(
            _ => Task.CompletedTask,
            limiter,
            logger);
        var first = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, "{\"inviteCode\":\"short\"}");
        var second = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, "{\"other\":true}");
        var third = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, "not-json");

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);
        await middleware.InvokeAsync(third);

        Assert.Equal(StatusCodes.Status400BadRequest, first.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, second.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, third.Response.StatusCode);
        var log = Assert.Single(logger.Entries);
        Assert.Contains("INVALID_BODY_BUCKET_LIMIT", log, StringComparison.Ordinal);
        Assert.DoesNotContain("not-json", log, StringComparison.Ordinal);
        Assert.Equal(0, first.Request.Body.Position);
        Assert.Equal(0, second.Request.Body.Position);
        Assert.Equal(0, third.Request.Body.Position);
    }

    [Fact]
    public async Task Noncanonical_base64url_final_bits_use_the_invalid_bucket()
    {
        var limiter = CreateLimiter(invalidPermitLimit: 1);
        var calls = 0;
        var middleware = new ActivationInviteRateLimitMiddleware(
            _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
            limiter,
            NullLogger<ActivationInviteRateLimitMiddleware>.Instance);
        var canonical = InviteCodeCodec.Generate();
        var nonCanonical = $"{canonical[..^1]}B";
        var first = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            $"{{\"inviteCode\":\"{nonCanonical}\"}}");
        var second = CreateContext(
            ActivationInviteRateLimitMiddleware.ExactPath,
            $"{{\"inviteCode\":\"{nonCanonical}\"}}");

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        Assert.Equal(0, calls);
        Assert.Equal(StatusCodes.Status400BadRequest, first.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
    }

    [Fact]
    public async Task Repeated_same_code_is_throttled_by_the_same_hash_shard()
    {
        var limiter = CreateLimiter(shardPermitLimit: 2);
        var downstreamCalls = 0;
        var logger = new RecordingLogger<ActivationInviteRateLimitMiddleware>();
        var middleware = new ActivationInviteRateLimitMiddleware(
            _ =>
            {
                downstreamCalls++;
                return Task.CompletedTask;
            },
            limiter,
            logger);
        var code = InviteCodeCodec.Generate();
        var body = $"{{\"inviteCode\":\"{code}\"}}";
        var first = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
        var second = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
        var third = CreateContext(ActivationInviteRateLimitMiddleware.ExactPath, body);
        third.TraceIdentifier = "activation-correlation";
        third.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.55");
        third.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.Subject, "sensitive-firebase-uid"),
            new Claim(FirebaseClaimTypes.Email, "sensitive@example.test"),
            new Claim(FirebaseClaimTypes.EmailVerified, bool.TrueString)
        }, "Bearer"));

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);
        await middleware.InvokeAsync(third);

        Assert.Equal(2, downstreamCalls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, third.Response.StatusCode);
        Assert.Equal("60", third.Response.Headers.RetryAfter);
        Assert.DoesNotContain(code, ReadResponse(third), StringComparison.Ordinal);
        var log = Assert.Single(logger.Entries);
        Assert.Contains("INVITE_HASH_SHARD_LIMIT", log, StringComparison.Ordinal);
        Assert.Contains("activation-correlation", log, StringComparison.Ordinal);
        Assert.DoesNotContain(code, log, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-firebase-uid", log, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive@example.test", log, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.55", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Random_valid_codes_cannot_create_unbounded_partitions()
    {
        var limiter = CreateLimiter(shardCount: 8, shardPermitLimit: 1000);
        var originalPartitions = limiter.PartitionCount;

        foreach (var code in Enumerable.Range(0, 500).Select(_ => InviteCodeCodec.Generate()))
        {
            var shard = limiter.GetShard(Encoding.ASCII.GetBytes(code));
            _ = limiter.TryAcquireShard(shard);
        }

        Assert.Equal(9, originalPartitions);
        Assert.Equal(originalPartitions, limiter.PartitionCount);
        Assert.Equal(8, limiter.ShardCount);
    }

    [Fact]
    public void Activate_action_uses_distinct_builtin_uid_ip_policy()
    {
        var action = typeof(AuthController).GetMethod(nameof(AuthController.ActivateApp));
        var rateLimit = Assert.Single(action!.GetCustomAttributes(
            typeof(EnableRateLimitingAttribute),
            inherit: true).Cast<EnableRateLimitingAttribute>());

        Assert.Equal("Activation", rateLimit.PolicyName);
    }

    [Fact]
    public void Program_pipeline_charges_activation_shard_after_authentication_and_endpoint_limiter()
    {
        var program = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "src",
            "GymTrackPro.API",
            "Program.cs"));
        var forwarded = program.IndexOf("app.UseForwardedHeaders()", StringComparison.Ordinal);
        var errors = program.IndexOf("app.UseMiddleware<ExceptionHandlingMiddleware>()", StringComparison.Ordinal);
        var routing = program.IndexOf("app.UseRouting()", StringComparison.Ordinal);
        var preAuthentication = program.IndexOf(
            "app.UseMiddleware<PreAuthenticationRateLimitMiddleware>()",
            StringComparison.Ordinal);
        var authentication = program.IndexOf("app.UseAuthentication()", StringComparison.Ordinal);
        var activation = program.IndexOf("app.UseMiddleware<ActivationInviteRateLimitMiddleware>()", StringComparison.Ordinal);
        var builtin = program.IndexOf("app.UseRateLimiter()", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization()", StringComparison.Ordinal);

        Assert.True(forwarded < errors);
        Assert.True(errors < routing);
        Assert.True(routing < preAuthentication);
        Assert.True(preAuthentication < authentication);
        Assert.True(authentication < builtin);
        Assert.True(builtin < activation);
        Assert.True(activation < authorization);
    }

    [Fact]
    public void Middleware_telemetry_has_no_sensitive_output_fields()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "src",
            "GymTrackPro.API",
            "Security",
            "ActivationInviteRateLimitMiddleware.cs"));

        Assert.Contains("ILogger<ActivationInviteRateLimitMiddleware>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FirebaseUid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedEmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.", source, StringComparison.Ordinal);
    }

    private static ActivationInviteRateLimitMiddleware CreateMiddleware(RequestDelegate next) => new(
        next,
        CreateLimiter(),
        NullLogger<ActivationInviteRateLimitMiddleware>.Instance);

    private static ActivationInviteHashShardLimiter CreateLimiter(
        int shardCount = 8,
        int shardPermitLimit = 10,
        int invalidPermitLimit = 5) => new(
            shardCount,
            shardPermitLimit,
            invalidPermitLimit,
            TimeSpan.FromHours(1),
            TimeProvider.System);

    private static DefaultHttpContext CreateContext(string path, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        context.Response.Body = new MemoryStream();
        context.User = CreateVerifiedPrincipal();
        return context;
    }

    private static ClaimsPrincipal CreateVerifiedPrincipal() =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(FirebaseClaimTypes.Subject, "verified-test-uid"),
            new Claim(FirebaseClaimTypes.Email, "verified@example.test"),
            new Claim(FirebaseClaimTypes.EmailVerified, bool.TrueString)
        }, "Bearer"));

    private static DefaultHttpContext CreatePipelineContext(
        string path,
        string body,
        string partition,
        IServiceProvider requestServices)
    {
        var context = CreateContext(path, body);
        context.RequestServices = requestServices;
        context.Request.Headers["X-Test-Partition"] = partition;
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("Activation")),
            "Activation test endpoint"));
        return context;
    }

    private static DefaultHttpContext CreateRateLimitContext(
        IServiceProvider requestServices,
        string? policyName)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = requestServices,
            TraceIdentifier = "rate-limit-correlation",
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(FirebaseClaimTypes.Subject, "sensitive-uid"),
                new Claim(FirebaseClaimTypes.Email, "sensitive@example.test"),
                new Claim(FirebaseClaimTypes.EmailVerified, bool.TrueString)
            }, "Bearer"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.99");
        context.Response.Body = new MemoryStream();
        if (policyName is not null)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new EnableRateLimitingAttribute(policyName)),
                $"{policyName} test endpoint"));
        }

        return context;
    }

    private static FixedWindowRateLimiterOptions OnePermitWindow() => new()
    {
        PermitLimit = 1,
        Window = TimeSpan.FromHours(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static string ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new SinkLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class SinkLogger : ILogger
        {
            private readonly List<string> _entries;

            public SinkLogger(List<string> entries)
            {
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                _entries.Add(formatter(state, exception));
        }
    }
}
