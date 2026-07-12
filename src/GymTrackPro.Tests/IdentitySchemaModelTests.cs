using System.Linq;
using GymTrackPro.API.Data;
using GymTrackPro.API.Migrations;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Tests.AuthSecurity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GymTrackPro.Tests;

public sealed class IdentitySchemaModelTests
{
    private const string RedemptionMetadataConstraintName =
        "CK_AccountInvites_RedemptionMetadata";
    private const string LegacyRedemptionMetadataConstraintName =
        "CK_AccountInvites_UsedMetadataComplete";
    private const string RedemptionMetadataConstraintSql =
        "([UsedAtUtc] IS NULL AND [UsedByFirebaseUid] IS NULL AND [RedemptionOperationId] IS NULL) OR " +
        "([UsedAtUtc] IS NOT NULL AND [UsedByFirebaseUid] IS NOT NULL AND [RedemptionOperationId] IS NOT NULL AND " +
        "[RedemptionOperationId] <> CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))";

    [Fact]
    public void UserRole_PreservesLegacyOrdinals()
    {
        Assert.Equal(0, (int)UserRole.Administrator);
        Assert.Equal(1, (int)UserRole.Receptionist);
        Assert.Equal(2, (int)UserRole.GymGoer);
    }

    [Fact]
    public void UserModel_StagesNormalizationAndProtectsFirebaseAndMemberLinks()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(User));
        Assert.NotNull(entity);

        Assert.True(entity!.FindProperty(nameof(User.PasswordHash))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(User.NormalizedEmail))!.IsNullable);
        Assert.Equal(128, entity.FindProperty(nameof(User.FirebaseUid))!.GetMaxLength());

        var firebaseUidIndex = FindIndex(entity, nameof(User.FirebaseUid));
        Assert.True(firebaseUidIndex.IsUnique);
        Assert.Equal("[FirebaseUid] IS NOT NULL", firebaseUidIndex.GetFilter());
        Assert.Equal(
            "Latin1_General_100_BIN2",
            entity.FindProperty(nameof(User.FirebaseUid))!.GetCollation());

        var memberIndex = FindIndex(entity, nameof(User.MemberID));
        Assert.True(memberIndex.IsUnique);
        Assert.Equal("[MemberID] IS NOT NULL", memberIndex.GetFilter());

        var normalizedEmailIndex = FindIndex(entity, nameof(User.NormalizedEmail));
        Assert.True(normalizedEmailIndex.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", normalizedEmailIndex.GetFilter());
        Assert.Equal("UX_Users_NormalizedEmail", normalizedEmailIndex.GetDatabaseName());
        Assert.Equal(
            "Latin1_General_100_BIN2",
            entity.FindProperty(nameof(User.NormalizedEmail))!.GetCollation());

        var constraints = entity.GetCheckConstraints().Select(constraint => constraint.Name).ToList();
        Assert.Contains("CK_Users_Role", constraints);
        Assert.Contains("CK_Users_RoleMemberLink", constraints);
        Assert.Contains("CK_Users_FirebaseUidNotBlank", constraints);
        Assert.Contains("CK_Users_NormalizedEmailNotBlank", constraints);
        var roleMemberLink = entity.GetCheckConstraints().Single(constraint =>
            constraint.Name == "CK_Users_RoleMemberLink");
        Assert.Contains("[Role] = 2 AND [MemberID] IS NOT NULL", roleMemberLink.Sql);
        Assert.Contains("[Role] IN (0, 1) AND [MemberID] IS NULL", roleMemberLink.Sql);

        var memberForeignKey = entity.GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Single().Name == nameof(User.MemberID));
        Assert.True(memberForeignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, memberForeignKey.DeleteBehavior);
    }

    [Fact]
    public void AccountInviteModel_EnforcesDurableInviteIntegrity()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AccountInvite));
        Assert.NotNull(entity);

        Assert.Equal(
            nameof(AccountInvite.AccountInviteID),
            entity!.FindPrimaryKey()!.Properties.Single().Name);

        var tokenHash = entity.FindProperty(nameof(AccountInvite.TokenHash))!;
        Assert.Equal(typeof(byte[]), tokenHash.ClrType);
        Assert.Equal(32, tokenHash.GetMaxLength());
        Assert.Equal("binary(32)", tokenHash.GetColumnType());
        Assert.True(FindIndex(entity, nameof(AccountInvite.TokenHash)).IsUnique);

        var redemptionProperty = entity.FindProperty(nameof(AccountInvite.RedemptionOperationId))!;
        Assert.Equal(typeof(Guid?), redemptionProperty.ClrType);

        var redemptionIndex = FindIndex(entity, nameof(AccountInvite.RedemptionOperationId));
        Assert.True(redemptionIndex.IsUnique);
        Assert.Equal("[RedemptionOperationId] IS NOT NULL", redemptionIndex.GetFilter());

        var rowVersion = entity.FindProperty(nameof(AccountInvite.RowVersion))!;
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);

        Assert.Equal(128, entity.FindProperty(nameof(AccountInvite.UsedByFirebaseUid))!.GetMaxLength());
        Assert.Equal(
            "Latin1_General_100_BIN2",
            entity.FindProperty(nameof(AccountInvite.UsedByFirebaseUid))!.GetCollation());
        Assert.Equal(
            "Latin1_General_100_BIN2",
            entity.FindProperty(nameof(AccountInvite.NormalizedEmail))!.GetCollation());
        Assert.False(FindIndex(entity, nameof(AccountInvite.NormalizedEmail)).IsUnique);

        var constraints = entity.GetCheckConstraints().ToList();
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_ExactlyOneTarget");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_TargetRole");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_ExpiryAfterCreation");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_UsedOrRevoked");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_UsedTimestampAfterCreation");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_RevokedTimestampAfterCreation");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_NormalizedEmailNotBlank");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_PurposeNotBlank");
        Assert.Contains(constraints, constraint => constraint.Name == "CK_AccountInvites_UsedUidNotBlank");
        var redemptionMetadata = constraints.Single(
            constraint => constraint.Name == RedemptionMetadataConstraintName);
        Assert.Equal(RedemptionMetadataConstraintSql, redemptionMetadata.Sql);
        Assert.DoesNotContain(constraints, constraint =>
            constraint.Name == LegacyRedemptionMetadataConstraintName);
        var usedBeforeExpiry = constraints.Single(
            constraint => constraint.Name == "CK_AccountInvites_UsedBeforeExpiry");
        Assert.Contains("[UsedAtUtc] < [ExpiresAtUtc]", usedBeforeExpiry.Sql);
        var targetConstraint = constraints.Single(
            constraint => constraint.Name == "CK_AccountInvites_ExactlyOneTarget");
        Assert.Contains("TargetMemberID] IS NOT NULL", targetConstraint.Sql);
        Assert.Contains("TargetUserID] IS NOT NULL", targetConstraint.Sql);

        foreach (var foreignKey in entity.GetForeignKeys())
        {
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }
    }

    [Fact]
    public void MemberProjectionVersionModel_UsesOneRowPerMemberAndBoundedBigint()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MemberProjectionVersion));
        Assert.NotNull(entity);

        Assert.Equal(
            nameof(MemberProjectionVersion.MemberID),
            entity!.FindPrimaryKey()!.Properties.Single().Name);
        Assert.Equal("bigint", entity.FindProperty(nameof(MemberProjectionVersion.Version))!.GetColumnType());
        Assert.Equal(0L, entity.FindProperty(nameof(MemberProjectionVersion.Version))!.GetDefaultValue());
        var rowVersion = entity.FindProperty(nameof(MemberProjectionVersion.RowVersion))!;
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        var constraint = entity.GetCheckConstraints().Single(item =>
            item.Name == "CK_MemberProjectionVersions_VersionRange");
        Assert.Contains(MemberProjectionVersion.MaximumVersion.ToString(), constraint.Sql);
        var foreignKey = entity.GetForeignKeys().Single();
        Assert.True(foreignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void RequiredStaleSessionSetting_IsMigrationSeeded()
    {
        using var context = CreateContext();
        var settingEntity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(SystemSetting));

        var seed = settingEntity!.GetSeedData().Single(item =>
            Equals(item[nameof(SystemSetting.SettingKey)], "StaleSessionHours"));
        Assert.Equal("16", seed[nameof(SystemSetting.SettingValue)]);
    }

    [Fact]
    public void IdentityMigrationDesignerAndSnapshot_MatchCurrentWaveB1Shape()
    {
        using var context = CreateContext();
        var currentModel = context.GetService<IDesignTimeModel>().Model;
        var migrationModel = new StageFirebaseIdentityAndAccountInvites().TargetModel;
        var snapshotModel = new GymDbContextModelSnapshot().Model;

        foreach (var model in new[] { currentModel, migrationModel, snapshotModel })
        {
            var invite = model.FindEntityType(typeof(AccountInvite).FullName!);
            Assert.NotNull(invite);
            Assert.Equal(
                "binary(32)",
                invite!.FindProperty(nameof(AccountInvite.TokenHash))!.GetColumnType());
            Assert.Contains(invite.GetCheckConstraints(), item =>
                item.Name == "CK_AccountInvites_UsedBeforeExpiry");
            var redemptionMetadata = invite.GetCheckConstraints().Single(item =>
                item.Name == RedemptionMetadataConstraintName);
            Assert.Equal(RedemptionMetadataConstraintSql, redemptionMetadata.Sql);
            Assert.DoesNotContain(invite.GetCheckConstraints(), item =>
                item.Name == LegacyRedemptionMetadataConstraintName);

            var user = model.FindEntityType(typeof(User).FullName!);
            Assert.NotNull(user);
            Assert.True(FindIndex(user!, nameof(User.NormalizedEmail)).IsUnique);
            Assert.Equal(
                "Latin1_General_100_BIN2",
                user.FindProperty(nameof(User.FirebaseUid))!.GetCollation());
            Assert.Contains(user.GetCheckConstraints(), constraint =>
                constraint.Name == "CK_Users_RoleMemberLink");

            var version = model.FindEntityType(typeof(MemberProjectionVersion).FullName!);
            Assert.NotNull(version);
            Assert.Equal(
                "bigint",
                version!.FindProperty(nameof(MemberProjectionVersion.Version))!.GetColumnType());

            var settings = model.FindEntityType(typeof(SystemSetting).FullName!);
            Assert.Contains(settings!.GetSeedData(), item =>
                Equals(item[nameof(SystemSetting.SettingKey)], "StaleSessionHours")
                && Equals(item[nameof(SystemSetting.SettingValue)], "16"));
        }
    }

    [Fact]
    public void IdentityMigrationSourceAndScript_UseExactRedemptionMetadataConstraint()
    {
        var root = TestWorkspace.FindRoot();
        var migrationSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "GymTrackPro.API",
            "Migrations",
            "20260711204834_StageFirebaseIdentityAndAccountInvites.cs"));
        var script = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "migrations",
            "20260711204834_StageFirebaseIdentityAndAccountInvites.idempotent.sql"));

        Assert.Contains(
            $"table.CheckConstraint(\"{RedemptionMetadataConstraintName}\", \"{RedemptionMetadataConstraintSql}\");",
            migrationSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            LegacyRedemptionMetadataConstraintName,
            migrationSource,
            StringComparison.Ordinal);
        Assert.Contains(
            $"CONSTRAINT [{RedemptionMetadataConstraintName}] CHECK ({RedemptionMetadataConstraintSql}),",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            LegacyRedemptionMetadataConstraintName,
            script,
            StringComparison.Ordinal);
    }

    private static GymDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GymDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GymTrackProModelOnly;Trusted_Connection=True;")
            .Options;
        return new GymDbContext(options);
    }

    private static IIndex FindIndex(IEntityType entity, string propertyName)
    {
        return entity.GetIndexes()
            .Single(index => index.Properties.Count == 1 && index.Properties[0].Name == propertyName);
    }
}
