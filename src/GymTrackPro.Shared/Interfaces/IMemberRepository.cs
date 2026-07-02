using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IMemberRepository : IBaseRepository<Member>
{
    Task<Member?> GetByPhoneNumberAsync(string phoneNumber);
    Task<Member?> GetByEmailAsync(string email);
    Task<Member?> GetByQRCodeAsync(string qrCode);
    Task<(IEnumerable<Member> Items, int TotalCount)> GetPagedAsync(string? search, string? status, int page, int pageSize);
}
