using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Exposes administrative endpoints to manage system-wide settings.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.BackOffice)]
public class SettingsController : ControllerBase
{
    private readonly ISystemSettingService _settingsService;

    public SettingsController(ISystemSettingService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Retrieves all current system settings.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<SystemSettingDto>>), 200)]
    public async Task<IActionResult> GetAllSettings()
    {
        var settings = await _settingsService.GetAllSettingsAsync();
        return Ok(ApiResponse<IEnumerable<SystemSettingDto>>.SuccessResponse(settings, "System settings retrieved successfully."));
    }

    /// <summary>
    /// Updates a system configuration setting value. Restricted to Administrator role.
    /// </summary>
    [HttpPut("{key}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingDto updateDto)
    {
        try
        {
            await _settingsService.UpdateSettingAsync(key, updateDto.SettingValue);
            return Ok(ApiResponse<string>.SuccessResponse(key, $"System setting '{key}' updated successfully."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
    }
}
