using System;

namespace GymTrackPro.Mobile.Services;

public interface INetworkService
{
    bool IsConnected { get; }
    event EventHandler<bool> ConnectivityChanged;
}
