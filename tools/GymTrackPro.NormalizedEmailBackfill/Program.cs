using GymTrackPro.API.Data;
using GymTrackPro.NormalizedEmailBackfill;
using Microsoft.EntityFrameworkCore;

if (!NormalizedEmailBackfillCommandParser.TryParse(
        args,
        Environment.GetEnvironmentVariable,
        out var command)
    || command is null)
{
    await Console.Error.WriteLineAsync(NormalizedEmailBackfillCommandOutput.Rejected);
    return 2;
}

BackfillCancellationCoordinator? cancellation = null;
try
{
    using var cancellationScope =
        BackfillCancellationCoordinator.RegisterForCurrentProcess();
    cancellation = cancellationScope;
    var options = new DbContextOptionsBuilder<GymDbContext>()
        .UseSqlServer(
            command.ConnectionString,
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null))
        .Options;
    await using var context = new GymDbContext(options);
    var store = new SqlServerNormalizedEmailBackfillStore(context);
    var service = new NormalizedEmailBackfillService(store);
    var result = await service.ExecuteAsync(
        command.Mode,
        command.BatchSize,
        command.ExpectedFingerprint,
        cancellationScope.Token);

    cancellationScope.Token.ThrowIfCancellationRequested();
    await Console.Out.WriteLineAsync(
        NormalizedEmailBackfillCommandOutput.Format(result).AsMemory(),
        cancellationScope.Token);
    cancellationScope.Token.ThrowIfCancellationRequested();
    return result.HasBlockingFindings ? 3 : 0;
}
catch (OperationCanceledException) when (cancellation?.WasCancellationRequested == true)
{
    await Console.Error.WriteLineAsync(NormalizedEmailBackfillCommandOutput.Canceled);
    return NormalizedEmailBackfillProcessExitCodes.Canceled;
}
catch (Exception) when (cancellation?.WasCancellationRequested == true)
{
    // Some providers surface canceled I/O as a provider-specific exception.
    // The operator signal remains the controlling process outcome; never echo
    // the provider exception because it may include topology or row data.
    await Console.Error.WriteLineAsync(NormalizedEmailBackfillCommandOutput.Canceled);
    return NormalizedEmailBackfillProcessExitCodes.Canceled;
}
catch
{
    // Never write provider or row-level exception text: either can include
    // credentials, topology, or identity data.
    await Console.Error.WriteLineAsync(NormalizedEmailBackfillCommandOutput.Failed);
    return 1;
}
