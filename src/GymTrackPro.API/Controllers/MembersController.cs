using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Services;
using GymTrackPro.API.Authorization;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides endpoints for managing gym member profiles.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.BackOffice)]
public class MembersController : ControllerBase
{
    private readonly IMemberService _memberService;
    private readonly IAuthenticationService _authService;
    private readonly ICurrentUserContext _currentUser;

    public MembersController(IMemberService memberService, IAuthenticationService authService, ICurrentUserContext currentUser)
    {
        _memberService = memberService;
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Retrieves all active and registered member profiles.
    /// </summary>
    /// <returns>A standardized API response listing the members.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MemberResponseDto>>), 200)]
    public async Task<IActionResult> GetAll()
    {
        var members = await _memberService.GetAllAsync();
        return Ok(ApiResponse<IEnumerable<MemberResponseDto>>.SuccessResponse(members));
    }

    /// <summary>
    /// Retrieves a specific member profile by their primary identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the member.</param>
    /// <returns>A standardized API response containing the member's profile.</returns>
    /// <response code="200">If the member is found.</response>
    /// <response code="404">If the member does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<MemberResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetById(int id)
    {
        var member = await _memberService.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound(ApiResponse.FailureResponse("Member not found."));
        }
        return Ok(ApiResponse<MemberResponseDto>.SuccessResponse(member));
    }

    /// <summary>
    /// Retrieves a specific member profile by their unique QR check-in code.
    /// </summary>
    /// <param name="request">Body containing the QR code assigned to the member.</param>
    /// <returns>A standardized API response containing the member's profile.</returns>
    /// <response code="200">If the member is found.</response>
    /// <response code="404">If no member matches the QR code.</response>
    [HttpPost("qr/lookup")]
    [ProducesResponseType(typeof(ApiResponse<MemberResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetByQRCode([FromBody] QrCodeLookupRequestDto request)
    {
        var member = await _memberService.GetByQRCodeAsync(request.QrCode);
        if (member == null)
        {
            return NotFound(ApiResponse.FailureResponse("Member not found for the provided QR code."));
        }
        return Ok(ApiResponse<MemberResponseDto>.SuccessResponse(member));
    }

    /// <summary>
    /// Registers and registers a new gym member.
    /// </summary>
    /// <param name="createDto">The profile creation parameters.</param>
    /// <returns>A standardized API response containing the newly registered member profile.</returns>
    /// <response code="201">If the member was successfully created.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<MemberResponseDto>), 201)]
    public async Task<IActionResult> Create([FromBody] CreateMemberDto createDto)
    {
        var member = await _memberService.CreateMemberAsync(createDto);
        return CreatedAtAction(nameof(GetById), new { id = member.MemberID }, ApiResponse<MemberResponseDto>.SuccessResponse(member, "Member created successfully."));
    }

    /// <summary>
    /// Updates an existing member profile's contact and registration information.
    /// </summary>
    /// <param name="id">The unique identifier of the member to update.</param>
    /// <param name="updateDto">The updated profile parameters.</param>
    /// <returns>A standardized API response containing the updated member profile.</returns>
    /// <response code="200">If the member was updated successfully.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<MemberResponseDto>), 200)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMemberDto updateDto)
    {
        var member = await _memberService.UpdateMemberAsync(id, updateDto);
        return Ok(ApiResponse<MemberResponseDto>.SuccessResponse(member, "Member updated successfully."));
    }

    /// <summary>
    /// Searches and filters member profiles with pagination support.
    /// </summary>
    /// <param name="search">The search term matching name, phone, member ID, or QR code.</param>
    /// <param name="status">The member status filter.</param>
    /// <param name="page">The page number (1-indexed).</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A standardized API response containing the paginated search results.</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<PagedResultDto<MemberResponseDto>>), 200)]
    public async Task<IActionResult> Search([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var result = await _memberService.GetPagedMembersAsync(search, status, page, pageSize);
        return Ok(ApiResponse<PagedResultDto<MemberResponseDto>>.SuccessResponse(result));
    }

    /// <summary>
    /// Soft deletes an existing member profile.
    /// </summary>
    /// <param name="id">The unique identifier of the member to delete.</param>
    /// <returns>A standardized API response indicating successful deletion.</returns>
    /// <response code="200">If the member was soft-deleted successfully.</response>
    /// <response code="404">If the member does not exist.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _memberService.DeleteMemberAsync(id);
        if (!deleted)
        {
            return NotFound(ApiResponse.FailureResponse("Member not found."));
        }
        return Ok(ApiResponse.SuccessResponse("Member deleted successfully."));
    }

    [HttpPost("{id:int}/app-invite")]
    [Authorize(Policy = Policies.BackOffice)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<AppInviteCodeResponseDto>), 200)]
    public async Task<IActionResult> CreateMemberInvite(int id, [FromBody] CreateAppInviteDto dto)
    {
        var response = await _authService.CreateMemberInviteAsync(id, GetRequiredActorUserId(), dto);
        return Ok(ApiResponse<AppInviteCodeResponseDto>.SuccessResponse(response, "Member invite created."));
    }

    [HttpGet("{id:int}/app-invite/status")]
    [Authorize(Policy = Policies.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<AppInviteResponseDto>), 200)]
    public async Task<IActionResult> GetMemberInviteStatus(int id)
    {
        var response = await _authService.GetMemberInviteStatusAsync(id);
        return Ok(ApiResponse<AppInviteResponseDto>.SuccessResponse(response, "Member invite status retrieved."));
    }

    [HttpDelete("{id:int}/app-invite")]
    [Authorize(Policy = Policies.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> RevokeMemberInvite(int id)
    {
        await _authService.RevokeMemberInviteAsync(id, GetRequiredActorUserId());
        return Ok(ApiResponse<object>.SuccessResponse(new object(), "Member invite revoked."));
    }

    private int GetRequiredActorUserId() => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");
}
