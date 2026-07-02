using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class PaymentRepository : BaseRepository<Payment>, IPaymentRepository
{
    public PaymentRepository(GymDbContext context) : base(context)
    {
    }

    public override async Task<Payment?> GetByIdAsync(int id)
    {
        return await _dbSet
            .Include(p => p.Member)
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.PaymentID == id && !p.IsDeleted);
    }

    public override async Task<IEnumerable<Payment>> GetAllAsync()
    {
        return await _dbSet.Where(p => !p.IsDeleted).ToListAsync();
    }

    public async Task<IEnumerable<Payment>> GetByMemberIdAsync(int memberId)
    {
        return await _dbSet
            .Where(p => p.MemberID == memberId && !p.IsDeleted)
            .ToListAsync();
    }

    public async Task<Payment?> GetByReceiptNumberAsync(string receiptNumber)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.ReceiptNumber == receiptNumber && !p.IsDeleted);
    }

    public override async Task DeleteAsync(Payment entity)
    {
        entity.IsDeleted = true;
        entity.LastModified = DateTime.UtcNow;
        await UpdateAsync(entity);
    }
}
