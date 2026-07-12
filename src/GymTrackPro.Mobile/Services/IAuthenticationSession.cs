using System.Threading;
using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services;

/// <summary>
/// Minimal session contract consumed by authenticated HTTP infrastructure.
/// Keeping this separate from the Firebase UI operations makes the handler
/// deterministic and independently testable.
/// </summary>
public interface IAuthenticationSession
{
    string? CurrentUserId { get; }

    Task<string?> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<bool> HasSessionAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
