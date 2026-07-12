using System.Xml.Linq;

namespace GymTrackPro.Mobile.Tests;

public sealed class BackupPolicyTests
{
    [Fact]
    public void Android_legacy_backup_excludes_database_preferences_and_appdata_files()
    {
        var document = XDocument.Load(GetFixturePath("Android", "backup_rules.xml"));
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("full-backup-content", root.Name.LocalName);
        AssertCurrentPersistenceExcluded(root);
    }

    [Fact]
    public void Android_modern_backup_and_device_transfer_exclude_current_persistence()
    {
        var document = XDocument.Load(GetFixturePath("Android", "data_extraction_rules.xml"));
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("data-extraction-rules", root.Name.LocalName);
        var cloudBackup = Assert.Single(root.Elements("cloud-backup"));
        var deviceTransfer = Assert.Single(root.Elements("device-transfer"));
        AssertCurrentPersistenceExcluded(cloudBackup);
        AssertCurrentPersistenceExcluded(deviceTransfer);
    }

    [Theory]
    [InlineData("iOS")]
    [InlineData("MacCatalyst")]
    public void Apple_launch_establishes_backup_exclusion_before_Maui_or_fails_closed(string platform)
    {
        // This is deliberately a source-policy test: Foundation/xattr behavior cannot
        // execute in the platform-neutral host and remains an Apple device gate.
        var policySource = File.ReadAllText(GetFixturePath("Apple", platform, "AppleBackupExclusion.cs"));
        var delegateSource = File.ReadAllText(GetFixturePath("Apple", platform, "AppDelegate.cs"));

        Assert.Contains("internal static bool Apply(out string? errorMessage)", policySource);
        Assert.Contains("Directory.CreateDirectory(path)", policySource);
        Assert.Contains("NSFileManager.SetSkipBackupAttribute(path, true)", policySource);
        Assert.Contains("FileSystem.AppDataDirectory", policySource);
        Assert.Contains("FileSystem.CacheDirectory", policySource);
        Assert.Contains("return failures.Count == 0;", policySource);
        Assert.DoesNotContain("Debug.WriteLine", policySource);
        Assert.DoesNotContain("encrypted SQLite", policySource, StringComparison.OrdinalIgnoreCase);

        var applyIndex = delegateSource.IndexOf(
            "if (!AppleBackupExclusion.Apply(out var errorMessage))",
            StringComparison.Ordinal);
        var diagnosticIndex = delegateSource.IndexOf("Console.Error.WriteLine", StringComparison.Ordinal);
        var abortIndex = delegateSource.IndexOf("return false;", StringComparison.Ordinal);
        var mauiLaunchIndex = delegateSource.IndexOf(
            "return base.FinishedLaunching(application, launchOptions);",
            StringComparison.Ordinal);

        Assert.True(applyIndex >= 0, "Apple launch must check the backup exclusion result.");
        Assert.True(diagnosticIndex > applyIndex, "Failure must emit a production-visible diagnostic.");
        Assert.True(abortIndex > diagnosticIndex, "Failure must abort launch after reporting the diagnostic.");
        Assert.True(mauiLaunchIndex > abortIndex, "MAUI must not construct App before exclusion succeeds.");
    }

    private static void AssertCurrentPersistenceExcluded(XElement container)
    {
        Assert.Empty(container.Elements("include"));
        AssertExclusion(container, "database");
        AssertExclusion(container, "sharedpref");
        AssertExclusion(container, "file");
    }

    private static void AssertExclusion(XElement container, string domain)
    {
        Assert.Contains(
            container.Elements("exclude"),
            element => string.Equals((string?)element.Attribute("domain"), domain, StringComparison.Ordinal) &&
                       string.Equals((string?)element.Attribute("path"), ".", StringComparison.Ordinal));
    }

    private static string GetFixturePath(params string[] segments)
    {
        return Path.Combine(new[] { AppContext.BaseDirectory, "PolicyFixtures" }.Concat(segments).ToArray());
    }
}
