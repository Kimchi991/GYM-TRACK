using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IMemberService
{
    Task<MemberResponseDto?> GetByIdAsync(int id);
    Task<MemberResponseDto?> GetByQRCodeAsync(string qrCode);
    Task<IEnumerable<MemberResponseDto>> GetAllAsync();
    Task<PagedResultDto<MemberResponseDto>> GetPagedMembersAsync(string? search, string? status, int page, int pageSize);
    Task<MemberResponseDto> CreateMemberAsync(CreateMemberDto createDto);
    Task<MemberResponseDto> UpdateMemberAsync(int id, UpdateMemberDto updateDto);
    Task<bool> DeleteMemberAsync(int id);
}
