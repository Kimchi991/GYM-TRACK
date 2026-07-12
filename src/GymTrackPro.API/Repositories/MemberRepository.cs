using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class MemberRepository : BaseRepository<Member>, IMemberRepository
{
    public MemberRepository(GymDbContext context) : base(context)
    {
    }

    public override async Task AddAsync(Member entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbSet.AddAsync(entity);
        _context.MemberProjectionVersions.Add(new MemberProjectionVersion
        {
            Member = entity,
            Version = 0
        });
        await _context.SaveChangesAsync();
    }

    public async Task<Member?> GetByPhoneNumberAsync(string phoneNumber)
    {
        return await _dbSet.FirstOrDefaultAsync(m => m.PhoneNumber == phoneNumber && !m.IsDeleted);
    }

    public async Task<Member?> GetByEmailAsync(string email)
    {
        return await _dbSet.FirstOrDefaultAsync(m => m.Email == email && !m.IsDeleted);
    }

    public async Task<Member?> GetByQRCodeAsync(string qrCode)
    {
        return await _dbSet.FirstOrDefaultAsync(m => m.QRCode == qrCode && !m.IsDeleted);
    }

    public async Task<(IEnumerable<Member> Items, int TotalCount)> GetPagedAsync(string? search, string? status, int page, int pageSize)
    {
        var query = _dbSet.AsNoTracking().Where(m => !m.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            bool isInt = int.TryParse(search, out int searchId);

            query = query.Where(m =>
                m.FirstName.ToLower().Contains(searchLower) ||
                m.LastName.ToLower().Contains(searchLower) ||
                m.PhoneNumber.Contains(search) ||
                (isInt && m.MemberID == searchId)
            );
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    // Type-safe signature match for IBaseRepository<Member>
    public override async Task<Member?> GetByIdAsync(int id)
    {
        var member = await base.GetByIdAsync(id);
        return member != null && !member.IsDeleted ? member : null;
    }

    public override async Task<IEnumerable<Member>> GetAllAsync()
    {
        return await _dbSet.Where(m => !m.IsDeleted).ToListAsync();
    }

    public override Task DeleteAsync(Member entity)
    {
        throw new NotSupportedException(
            "Member deletion must use the atomic deletion and access-revocation transaction.");
    }
}
