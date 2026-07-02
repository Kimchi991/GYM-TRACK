using System;
using System.Threading.Tasks;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Services;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task LogActivityAsync(int? userId, string action, string details, string ipAddress)
    {
        var log = new AuditLog
        {
            UserID = userId,
            Action = action,
            Details = details,
            IPAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log);
    }
}
