using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class ClockService : IClockService
{
    public DateTime UtcNow => DateTime.UtcNow;
}
