namespace GymTrackPro.NormalizedEmailBackfill;

internal interface IBackfillCancellationRegistrar
{
    IDisposable Register(Func<bool> shouldSuppressTermination);
}

internal sealed class SystemConsoleCancellationRegistrar : IBackfillCancellationRegistrar
{
    internal static SystemConsoleCancellationRegistrar Instance { get; } = new();

    private SystemConsoleCancellationRegistrar()
    {
    }

    public IDisposable Register(Func<bool> shouldSuppressTermination) =>
        new ConsoleRegistration(shouldSuppressTermination);

    private sealed class ConsoleRegistration : IDisposable
    {
        private ConsoleCancelEventHandler? _handler;

        internal ConsoleRegistration(Func<bool> shouldSuppressTermination)
        {
            ArgumentNullException.ThrowIfNull(shouldSuppressTermination);
            _handler = (_, eventArgs) =>
                eventArgs.Cancel = shouldSuppressTermination();
            Console.CancelKeyPress += _handler;
        }

        public void Dispose()
        {
            var handler = Interlocked.Exchange(ref _handler, null);
            if (handler is not null)
            {
                Console.CancelKeyPress -= handler;
            }
        }
    }
}

internal sealed class BackfillCancellationCoordinator : IDisposable
{
    private readonly CancellationTokenSource _cancellationSource = new();
    private IDisposable? _registration;
    private int _signalCount;
    private int _disposeState;

    internal BackfillCancellationCoordinator(IBackfillCancellationRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        _registration = registrar.Register(HandleSignal);
    }

    internal CancellationToken Token => _cancellationSource.Token;

    internal bool WasCancellationRequested => Volatile.Read(ref _signalCount) > 0;

    internal static BackfillCancellationCoordinator RegisterForCurrentProcess() =>
        new(SystemConsoleCancellationRegistrar.Instance);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref _registration, null)?.Dispose();
        }
        finally
        {
            _cancellationSource.Dispose();
        }
    }

    private bool HandleSignal()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return false;
        }

        if (Interlocked.Increment(ref _signalCount) != 1)
        {
            // A second Ctrl+C is intentionally not suppressed, allowing the
            // operating system's conventional forced-termination behavior.
            return false;
        }

        try
        {
            _cancellationSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Disposal won the race and already removed the global handler.
            return false;
        }
        catch (AggregateException)
        {
            // A cancellation callback failed, but the token is already canceled.
            // Keep the first signal suppressed so normal unwinding can report a
            // generic outcome without leaking callback/provider details.
            return true;
        }
    }
}

internal static class NormalizedEmailBackfillProcessExitCodes
{
    internal const int Canceled = 130;
}
