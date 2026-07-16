namespace Yanban.Application.Abstractions;

/// <summary>What one board is allowed to store.</summary>
public record BoardQuota(long MaxFileBytes, long MaxBoardBytes);

/// <summary>
/// The limits are the same for everyone today, and the default implementation reads them from
/// configuration. Behind this interface, a per-board or per-plan policy can drop in without
/// touching <c>AttachmentService</c>. It takes a boardId for that reason: a policy that could not
/// vary by board would not need one.
/// </summary>
public interface IBoardQuotaPolicy
{
    Task<BoardQuota> GetAsync(Guid boardId, CancellationToken ct);
}
