using System.Threading.Tasks;

namespace GymTrackPro.Shared.Interfaces;

public interface IAuditService
{
    Task LogActivityAsync(int? userId, string action, string details, string ipAddress);
}
