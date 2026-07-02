using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IDashboardService
{
    Task<DashboardMetricsDto> GetDashboardMetricsAsync();
}
