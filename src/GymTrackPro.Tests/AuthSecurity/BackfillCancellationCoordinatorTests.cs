using GymTrackPro.NormalizedEmailBackfill;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class BackfillCancellationCoordinatorTests
{
    [Fact]
    public void First_signal_requests_graceful_cancellation_and_second_allows_termination()
    {
        var registrar = new FakeCancellationRegistrar();
        using var coordinator = new BackfillCancellationCoordinator(registrar);

        Assert.False(coordinator.Token.IsCancellationRequested);
        Assert.True(registrar.Signal());
        Assert.True(coordinator.Token.IsCancellationRequested);
        Assert.True(coordinator.WasCancellationRequested);
        Assert.False(registrar.Signal());
    }

    [Fact]
    public void Disposal_unregisters_once_and_leaves_no_handler_reference()
    {
        var registrar = new FakeCancellationRegistrar();
        var coordinator = new BackfillCancellationCoordinator(registrar);
        Assert.Equal(1, registrar.ActiveRegistrationCount);

        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Equal(1, registrar.DisposeCount);
        Assert.Equal(0, registrar.ActiveRegistrationCount);
        Assert.False(registrar.TrySignal(out _));
    }

    [Fact]
    public void Canceled_process_contract_uses_conventional_exit_and_generic_output()
    {
        Assert.Equal(130, NormalizedEmailBackfillProcessExitCodes.Canceled);
        Assert.Equal(
            "Normalized-email backfill command canceled.",
            NormalizedEmailBackfillCommandOutput.Canceled);
        Assert.DoesNotContain("@", NormalizedEmailBackfillCommandOutput.Canceled, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", NormalizedEmailBackfillCommandOutput.Canceled, StringComparison.OrdinalIgnoreCase);

        var program = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "tools",
            "GymTrackPro.NormalizedEmailBackfill",
            "Program.cs"));
        Assert.Contains("catch (OperationCanceledException)", program, StringComparison.Ordinal);
        Assert.Contains(
            "return NormalizedEmailBackfillProcessExitCodes.Canceled",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_propagates_cancellation_token_to_store_reads()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CancelingReadStore(cancellation);
        var service = new NormalizedEmailBackfillService(store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 1,
            expectedFingerprint: null,
            cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, store.ObservedToken);
        Assert.Equal(1, store.ReadCount);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task Service_propagates_cancellation_token_to_confirmed_batch_apply()
    {
        var store = new CancelingApplyStore();
        var service = new NormalizedEmailBackfillService(store);
        var dryRun = await service.ExecuteAsync(
            NormalizedEmailBackfillMode.DryRun,
            batchSize: 2);
        using var cancellation = new CancellationTokenSource();
        store.CancelOnApply(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            NormalizedEmailBackfillMode.Confirm,
            batchSize: 2,
            expectedFingerprint: dryRun.InitialAnalysis.SnapshotFingerprint,
            cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, store.ObservedApplyToken);
        Assert.Equal(1, store.ApplyCount);
    }

    private sealed class FakeCancellationRegistrar : IBackfillCancellationRegistrar
    {
        private Func<bool>? _handler;

        public int ActiveRegistrationCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IDisposable Register(Func<bool> shouldSuppressTermination)
        {
            Assert.Null(_handler);
            _handler = shouldSuppressTermination;
            ActiveRegistrationCount++;
            return new CallbackDisposable(() =>
            {
                _handler = null;
                ActiveRegistrationCount--;
                DisposeCount++;
            });
        }

        public bool Signal() =>
            _handler?.Invoke()
            ?? throw new InvalidOperationException("No cancellation handler is registered.");

        public bool TrySignal(out bool shouldSuppressTermination)
        {
            if (_handler is null)
            {
                shouldSuppressTermination = false;
                return false;
            }

            shouldSuppressTermination = _handler();
            return true;
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? _dispose;

            internal CallbackDisposable(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose() =>
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed class CancelingReadStore : INormalizedEmailBackfillStore
    {
        private readonly CancellationTokenSource _cancellation;

        internal CancelingReadStore(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public CancellationToken ObservedToken { get; private set; }
        public int ReadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public Task<IReadOnlyList<NormalizedEmailBackfillRow>> ReadBatchAsync(
            int afterUserId,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            ObservedToken = cancellationToken;
            _cancellation.Cancel();
            return Task.FromCanceled<IReadOnlyList<NormalizedEmailBackfillRow>>(
                cancellationToken);
        }

        public Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchAsync(
            int afterUserId,
            int batchSize,
            IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            throw new InvalidOperationException("Apply should not run after read cancellation.");
        }
    }

    private sealed class CancelingApplyStore : INormalizedEmailBackfillStore
    {
        private CancellationTokenSource? _cancellation;

        public CancellationToken ObservedApplyToken { get; private set; }
        public int ApplyCount { get; private set; }

        public void CancelOnApply(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public Task<IReadOnlyList<NormalizedEmailBackfillRow>> ReadBatchAsync(
            int afterUserId,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<NormalizedEmailBackfillRow> rows = afterUserId == 0
                ? new[]
                {
                    new NormalizedEmailBackfillRow(
                        1,
                        "cancellation@example.test",
                        NormalizedEmail: null)
                }
                : Array.Empty<NormalizedEmailBackfillRow>();
            return Task.FromResult(rows);
        }

        public Task<NormalizedEmailBackfillBatchResult> ApplyNextBatchAsync(
            int afterUserId,
            int batchSize,
            IReadOnlyList<NormalizedEmailBackfillExpectedRow> expectedRows,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            ObservedApplyToken = cancellationToken;
            var cancellation = _cancellation
                ?? throw new InvalidOperationException("Apply cancellation was not configured.");
            cancellation.Cancel();
            return Task.FromCanceled<NormalizedEmailBackfillBatchResult>(
                cancellationToken);
        }
    }
}
