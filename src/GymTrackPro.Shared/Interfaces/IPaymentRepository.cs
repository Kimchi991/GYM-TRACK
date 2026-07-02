using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IPaymentRepository : IBaseRepository<Payment>
{
    Task<IEnumerable<Payment>> GetByMemberIdAsync(int memberId);
    Task<Payment?> GetByReceiptNumberAsync(string receiptNumber);
}
