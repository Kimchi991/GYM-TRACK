using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (!BootstrapCommandParser.TryParse(
        args,
        Environment.GetEnvironmentVariable,
        out var command)
    || command is null)
{
    await Console.Error.WriteLineAsync("Bootstrap command rejected.");
    return 2;
}

BootstrapFirebaseIdentity firebaseIdentity;
try
{
    using var oidcClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    var tokenValidator = new FirebaseBootstrapTokenValidator(oidcClient);
    firebaseIdentity = await tokenValidator.ValidateAsync(
        command.FirebaseIdToken,
        command.FirebaseProjectId);
}
catch
{
    // Never echo token-validation details or the token-derived identity.
    await Console.Error.WriteLineAsync("Bootstrap command failed.");
    return 1;
}

var services = new ServiceCollection();
services.AddBootstrapCore(
    command,
    options => options.UseSqlServer(
        command.ConnectionString,
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null)));

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true
});
await using var scope = provider.CreateAsyncScope();
try
{
    var service = scope.ServiceProvider.GetRequiredService<IOwnerBootstrapService>();
    var result = await service.ExecuteAsync(
        new OwnerBootstrapRequest(
            command.UserId,
            firebaseIdentity.FirebaseUid,
            firebaseIdentity.NormalizedEmail,
            DryRun: command.Mode == BootstrapExecutionMode.DryRun,
            Confirm: command.Mode == BootstrapExecutionMode.Confirm),
        new IdentityOperationContext($"bootstrap-{Guid.NewGuid():N}", "MaintenanceCLI"));
    await Console.Out.WriteLineAsync(BootstrapCommandOutput.Format(result));
    return 0;
}
catch
{
    // Never write exception text: provider errors can contain connection metadata and the
    // bootstrap request contains sensitive identity values.
    await Console.Error.WriteLineAsync("Bootstrap command failed.");
    return 1;
}
