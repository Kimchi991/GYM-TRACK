using System.Reflection;
using System.Security.Claims;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Controllers;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Moq;

namespace GymTrackPro.Tests;

public sealed class ProfilePicturesControllerTests
{
    [Fact]
    public async Task Anonymous_principal_cannot_satisfy_profile_route_policy()
    {
        var requirement = new ActiveAppUserRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await new AppAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [InlineData(nameof(ProfilePicturesController.GetForBackOffice))]
    [InlineData(nameof(ProfilePicturesController.GetForCurrentMember))]
    public void Routes_require_an_active_user_and_never_allow_anonymous_access(string methodName)
    {
        var method = typeof(ProfilePicturesController).GetMethod(methodName);

        Assert.NotNull(method);
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(Policies.ActiveAppUser, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Gym_goer_requesting_backoffice_photo_gets_generic_not_found()
    {
        var fixture = CreateController(UserRole.GymGoer, memberId: 7);

        var result = await fixture.Controller.GetForBackOffice(8);

        Assert.IsType<NotFoundResult>(result);
        fixture.Repository.Verify(
            repository => repository.GetByIdAsync(It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task Backoffice_requesting_self_route_gets_generic_not_found()
    {
        var fixture = CreateController(UserRole.Receptionist, memberId: null);

        var result = await fixture.Controller.GetForCurrentMember();

        Assert.IsType<NotFoundResult>(result);
        fixture.Repository.Verify(
            repository => repository.GetByIdAsync(It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task Gym_goer_self_route_reads_only_claimed_member_photo()
    {
        var fixture = CreateController(UserRole.GymGoer, memberId: 7);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(7))
            .ReturnsAsync(CreateMember(7));
        fixture.Storage
            .Setup(storage => storage.OpenRead("profile:photo.jpg"))
            .Returns(new ProfilePictureContent(new MemoryStream([1, 2, 3]), "image/jpeg"));

        var result = await fixture.Controller.GetForCurrentMember();

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(
            "no-store, no-cache, must-revalidate",
            fixture.HttpContext.Response.Headers[HeaderNames.CacheControl].ToString());
        Assert.Equal(
            "nosniff",
            fixture.HttpContext.Response.Headers[HeaderNames.XContentTypeOptions].ToString());
        await file.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task Backoffice_can_read_member_photo()
    {
        var fixture = CreateController(UserRole.Administrator, memberId: null);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(9))
            .ReturnsAsync(CreateMember(9));
        fixture.Storage
            .Setup(storage => storage.OpenRead("profile:photo.jpg"))
            .Returns(new ProfilePictureContent(new MemoryStream([1]), "image/jpeg"));

        var result = await fixture.Controller.GetForBackOffice(9);

        var file = Assert.IsType<FileStreamResult>(result);
        await file.FileStream.DisposeAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Missing_member_or_file_returns_same_generic_not_found(
        bool memberExists,
        bool fileExists)
    {
        var fixture = CreateController(UserRole.Administrator, memberId: null);
        if (memberExists)
        {
            fixture.Repository
                .Setup(repository => repository.GetByIdAsync(11))
                .ReturnsAsync(CreateMember(11));
        }
        else
        {
            fixture.Repository
                .Setup(repository => repository.GetByIdAsync(11))
                .ReturnsAsync((Member?)null);
        }
        if (fileExists)
        {
            fixture.Storage
                .Setup(storage => storage.OpenRead(It.IsAny<string>()))
                .Returns(new ProfilePictureContent(new MemoryStream([1]), "image/jpeg"));
        }
        else if (memberExists)
        {
            fixture.Storage
                .Setup(storage => storage.OpenRead("profile:photo.jpg"))
                .Returns((ProfilePictureContent?)null);
        }

        var result = await fixture.Controller.GetForBackOffice(11);

        Assert.IsType<NotFoundResult>(result);
    }

    private static ControllerFixture CreateController(UserRole role, int? memberId)
    {
        var repository = new Mock<IMemberRepository>(MockBehavior.Strict);
        var storage = new Mock<IProfilePictureStorage>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(context => context.Role).Returns(role);
        currentUser.SetupGet(context => context.MemberId).Returns(memberId);
        var httpContext = new DefaultHttpContext();
        var controller = new ProfilePicturesController(
            repository.Object,
            storage.Object,
            currentUser.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        return new ControllerFixture(controller, repository, storage, httpContext);
    }

    private static Member CreateMember(int memberId) => new()
    {
        MemberID = memberId,
        ProfilePicture = "profile:photo.jpg",
        IsDeleted = false
    };

    private sealed record ControllerFixture(
        ProfilePicturesController Controller,
        Mock<IMemberRepository> Repository,
        Mock<IProfilePictureStorage> Storage,
        DefaultHttpContext HttpContext);
}
