using System;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IGymGoerProjectionService
{
    Task<GoerDashboardDto> GetGoerDashboardAsync(CancellationToken cancellationToken = default);
    Task<GoerDigitalCardDto> GetDigitalCardAsync(CancellationToken cancellationToken = default);
    Task<GoerProgressDto> GetProgressAsync(string month, CancellationToken cancellationToken = default);
}
