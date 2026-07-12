using GymTrackPro.Preflight;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class PreflightCommandSecurityTests
{
    private const string ExplicitSecureConnection =
        "Server=private-preflight-source;Database=private-preflight-catalog;" +
        "User Id=reader;Password=private-preflight-password;" +
        "Encrypt=True;TrustServerCertificate=False";

    [Fact]
    public void Command_shape_has_explicit_redacted_string_representation()
    {
        Assert.True(PreflightCommandParser.TryParse(
            new[] { "--mode", "pre-stage" },
            _ => ExplicitSecureConnection,
            out var command));

        var representation = command!.ToString();
        Assert.True(typeof(PreflightCommand).IsSealed);
        Assert.DoesNotContain(
            typeof(PreflightCommand).GetMethods(),
            method => method.Name == "Deconstruct");
        Assert.Contains("Mode=PreStage", representation, StringComparison.Ordinal);
        Assert.Contains("Connection=[REDACTED]", representation, StringComparison.Ordinal);
        Assert.DoesNotContain("private-preflight-source", representation, StringComparison.Ordinal);
        Assert.DoesNotContain("private-preflight-catalog", representation, StringComparison.Ordinal);
        Assert.DoesNotContain("private-preflight-password", representation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Server=db;Database=Gym;Encrypt=True")]
    [InlineData("Server=db;Database=Gym;TrustServerCertificate=False")]
    [InlineData("Server=db;Database=Gym;Encrypt=Strict")]
    public void Parser_rejects_implicit_transport_defaults(string connectionString)
    {
        Assert.False(PreflightCommandParser.TryParse(
            new[] { "--mode", "post-stage" },
            _ => connectionString,
            out var command));
        Assert.Null(command);
    }

    [Fact]
    public void Environment_provider_exception_and_console_contract_remain_secret_free()
    {
        const string secret = "private-provider-exception-value";
        Assert.False(PreflightCommandParser.TryParse(
            new[] { "--mode", "pre-stage" },
            _ => throw new InvalidOperationException(secret),
            out var command));
        Assert.Null(command);

        var program = File.ReadAllText(Path.Combine(
            TestWorkspace.FindRoot(),
            "tools",
            "GymTrackPro.Preflight",
            "Program.cs"));
        foreach (var output in new[]
                 {
                     PreflightCommandOutput.Rejected,
                     PreflightCommandOutput.Failed,
                     program
                 })
        {
            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("exception.Message", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Console.Out.WriteLineAsync(command", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.Error.WriteLineAsync(command", program, StringComparison.Ordinal);
    }
}
