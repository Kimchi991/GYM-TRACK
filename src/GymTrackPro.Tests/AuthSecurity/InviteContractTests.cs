using System.ComponentModel.DataAnnotations;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Controllers;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.Tests.AuthSecurity;

public sealed class InviteContractTests
{
    [Fact]
    public void Generated_codes_have_32_bytes_of_entropy_and_exact_base64url_shape()
    {
        var codes = Enumerable.Range(0, 128)
            .Select(_ => InviteCodeCodec.Generate())
            .ToArray();

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code =>
        {
            Assert.Equal(43, code.Length);
            Assert.True(InviteCodeCodec.IsValid(code));
            var padded = code.Replace('-', '+').Replace('_', '/') + "=";
            Assert.Equal(32, Convert.FromBase64String(padded).Length);
        });
    }

    [Fact]
    public void Hash_is_exact_raw_sha256_and_invalid_codes_are_never_hashed()
    {
        var code = InviteCodeCodec.Generate();

        Assert.True(InviteCodeCodec.TryHash(code, out var first));
        Assert.True(InviteCodeCodec.TryHash(code, out var second));
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.False(InviteCodeCodec.TryHash(code + "=", out var padded));
        Assert.Empty(padded);
        var nonCanonical = $"{code[..^1]}B";
        Assert.False(InviteCodeCodec.TryHash(nonCanonical, out var nonCanonicalHash));
        Assert.Empty(nonCanonicalHash);
        Assert.False(InviteCodeCodec.TryHash("short", out var shortHash));
        Assert.Empty(shortHash);
    }

    [Fact]
    public void Canonical_activation_dto_rejects_empty_operation_id()
    {
        var dto = new ActivateInviteDto
        {
            InviteCode = InviteCodeCodec.Generate(),
            OperationId = Guid.Empty
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            dto,
            new ValidationContext(dto),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(ActivateInviteDto.OperationId)));
    }

    [Fact]
    public void Account_invite_has_only_canonical_authority_fields_and_raw_hash_contract()
    {
        Assert.Equal(typeof(byte[]), typeof(AccountInvite).GetProperty(nameof(AccountInvite.TokenHash))!.PropertyType);
        Assert.Null(typeof(AccountInvite).GetProperty("InviteID"));
        Assert.Null(typeof(AccountInvite).GetProperty("CreatorUserID"));
        Assert.Null(typeof(AccountInvite).GetProperty("CreatedAt"));
        Assert.Null(typeof(AccountInvite).GetProperty("Expiry"));
        Assert.Null(typeof(AccountInvite).GetProperty("UsedAt"));
        Assert.Null(typeof(AccountInvite).GetProperty("RevokedAt"));
        Assert.Null(typeof(AccountInvite).GetProperty("RedemptionOperationID"));
    }

    [Fact]
    public void Legacy_activation_adapter_is_obsolete_and_declares_no_authority_properties()
    {
#pragma warning disable CS0618
        var adapterType = typeof(ActivateAppRequestDto);
#pragma warning restore CS0618
        Assert.NotNull(adapterType.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).SingleOrDefault());
        Assert.Empty(adapterType.GetProperties(System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.DeclaredOnly));
    }

    [Theory]
    [InlineData(typeof(MembersController), nameof(MembersController.CreateMemberInvite))]
    [InlineData(typeof(UsersController), nameof(UsersController.CreateUserInvite))]
    public void Plaintext_invite_code_responses_are_never_cacheable(
        Type controllerType,
        string actionName)
    {
        var action = controllerType.GetMethod(actionName);
        var responseCache = Assert.Single(
            action!.GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
                .Cast<ResponseCacheAttribute>());

        Assert.True(responseCache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, responseCache.Location);
    }
}
