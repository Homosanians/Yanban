using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Attachments;

/// <summary>
/// One quota for every board, read from configuration. Deliberately the dullest possible
/// implementation of <see cref="IBoardQuotaPolicy"/> — the interesting part is that the interface
/// lets it be replaced without the enforcement path caring.
/// </summary>
public class ConfiguredBoardQuotaPolicy : IBoardQuotaPolicy
{
    private readonly QuotaOptions _options;

    public ConfiguredBoardQuotaPolicy(IOptions<QuotaOptions> options) => _options = options.Value;

    public Task<BoardQuota> GetAsync(Guid boardId, CancellationToken ct) =>
        Task.FromResult(new BoardQuota(_options.MaxFileBytes, _options.MaxBoardBytes));
}
