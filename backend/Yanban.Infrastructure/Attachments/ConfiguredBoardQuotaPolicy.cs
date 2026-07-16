using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Attachments;

/// <summary>
/// One quota for every board, read from configuration. A per-board or per-plan policy can replace
/// this behind <see cref="IBoardQuotaPolicy"/> without the enforcement path caring.
/// </summary>
public class ConfiguredBoardQuotaPolicy : IBoardQuotaPolicy
{
    private readonly QuotaOptions _options;

    public ConfiguredBoardQuotaPolicy(IOptions<QuotaOptions> options) => _options = options.Value;

    public Task<BoardQuota> GetAsync(Guid boardId, CancellationToken ct) =>
        Task.FromResult(new BoardQuota(_options.MaxFileBytes, _options.MaxBoardBytes));
}
