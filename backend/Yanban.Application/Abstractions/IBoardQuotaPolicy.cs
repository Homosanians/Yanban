namespace Yanban.Application.Abstractions;

/// <summary>What one board is allowed to store.</summary>
public record BoardQuota(long MaxFileBytes, long MaxBoardBytes);

/// <summary>
/// The limits are the same for everyone today, and the default implementation just reads them from
/// configuration. This exists so that stays a *fact about the implementation* rather than a fact
/// about the call sites: a per-board or per-plan policy drops in behind this interface without
/// touching a line of <c>AttachmentService</c>.
///
/// <para>It takes a boardId for exactly that reason. A policy that could not vary by board would
/// not need one — and would have to be rewritten the first time it had to.</para>
/// </summary>
public interface IBoardQuotaPolicy
{
    Task<BoardQuota> GetAsync(Guid boardId, CancellationToken ct);
}
