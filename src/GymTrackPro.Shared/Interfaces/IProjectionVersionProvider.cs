namespace GymTrackPro.Shared.Interfaces;

/// <summary>
/// Reads the durable mutation counter for a member projection. Implementations must
/// use the same scoped database context/transaction as the projection read. The value
/// is incremented by each committed mutation that can affect that member; it is never
/// allocated by a GET request.
/// </summary>
public interface IProjectionVersionProvider
{
    Task<long> GetMutationVersionForMemberAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments and returns the member's durable mutation version.
    /// Relational callers should invoke this inside the same transaction as the
    /// mutation whose commit the version represents.
    /// </summary>
    Task<long> IncrementMutationVersionForMemberAsync(
        int memberId,
        CancellationToken cancellationToken = default);
}
