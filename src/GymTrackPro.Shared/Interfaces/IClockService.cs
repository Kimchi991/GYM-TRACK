using System;

namespace GymTrackPro.Shared.Interfaces;

public interface IClockService
{
    DateTime UtcNow { get; }
}
