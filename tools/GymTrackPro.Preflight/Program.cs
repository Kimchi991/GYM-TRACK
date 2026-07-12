using GymTrackPro.API.Data;
using GymTrackPro.API.Maintenance;
using GymTrackPro.Preflight;
using Microsoft.EntityFrameworkCore;

if (!PreflightCommandParser.TryParse(
        args,
        Environment.GetEnvironmentVariable,
        out var command)
    || command is null)
{
    await Console.Error.WriteLineAsync(PreflightCommandOutput.Rejected);
    return 2;
}

try
{
    var options = new DbContextOptionsBuilder<GymDbContext>()
        .UseSqlServer(
            command.ConnectionString,
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null))
        .Options;
    await using var context = new GymDbContext(options);
    var dataSource = new SqlServerPreflightReadOnlyDataSource(context);
    var runner = new MigrationPreflightRunner(dataSource);
    var report = await runner.RunAsync(command.Mode);
    await Console.Out.WriteLineAsync(MigrationPreflightReportFormatter.Format(report));
    return report.HasBlockingFindings ? 3 : 0;
}
catch
{
    // Provider failures may embed credentials or server metadata. Keep output generic.
    await Console.Error.WriteLineAsync(PreflightCommandOutput.Failed);
    return 1;
}
