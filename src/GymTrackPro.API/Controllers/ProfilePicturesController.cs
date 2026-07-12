using GymTrackPro.API.Authorization;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class ProfilePicturesController : ControllerBase
{
    private readonly IMemberRepository _memberRepository;
    private readonly IProfilePictureStorage _storage;
    private readonly ICurrentUserContext _currentUser;

    public ProfilePicturesController(
        IMemberRepository memberRepository,
        IProfilePictureStorage storage,
        ICurrentUserContext currentUser)
    {
        _memberRepository = memberRepository;
        _storage = storage;
        _currentUser = currentUser;
    }

    [HttpGet("members/{memberId:int}/profile-picture")]
    [Authorize(Policy = Policies.ActiveAppUser)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetForBackOffice(int memberId)
    {
        if (_currentUser.Role is not (UserRole.Administrator or UserRole.Receptionist))
        {
            return NotFound();
        }

        return await GetPictureAsync(memberId);
    }

    [HttpGet("me/profile-picture")]
    [Authorize(Policy = Policies.ActiveAppUser)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetForCurrentMember()
    {
        if (_currentUser.Role != UserRole.GymGoer
            || !_currentUser.MemberId.HasValue)
        {
            return NotFound();
        }

        return await GetPictureAsync(_currentUser.MemberId.Value);
    }

    private async Task<IActionResult> GetPictureAsync(int memberId)
    {
        if (memberId <= 0)
        {
            return NotFound();
        }

        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member is null || member.IsDeleted)
        {
            return NotFound();
        }

        var picture = _storage.OpenRead(member.ProfilePicture);
        if (picture is null)
        {
            return NotFound();
        }

        Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
        Response.Headers[HeaderNames.Pragma] = "no-cache";
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return File(picture.Stream, picture.ContentType, enableRangeProcessing: false);
    }
}
