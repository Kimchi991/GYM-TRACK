using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IMembershipPlanService
{
    Task<MembershipPlanResponseDto?> GetByIdAsync(int id);
    Task<IEnumerable<MembershipPlanResponseDto>> GetAllAsync();
    Task<MembershipPlanResponseDto> CreatePlanAsync(CreateMembershipPlanDto createDto);
    Task<MembershipPlanResponseDto> UpdatePlanAsync(int id, CreateMembershipPlanDto updateDto);
    Task DeletePlanAsync(int id);
}
