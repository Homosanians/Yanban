using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Application.Search;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Persistence.Configurations;

namespace Yanban.Infrastructure.Search;

public class CardSearchService : ICardSearchService
{
    private readonly YanbanDbContext _db;

    public CardSearchService(YanbanDbContext db) => _db = db;

    public async Task<IReadOnlyList<CardSearchHit>> SearchAsync(Guid boardId, string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationAppException("A search query is required.");

        // websearch_to_tsquery, never to_tsquery: it parses whatever the user typed and
        // cannot fail. to_tsquery raises `syntax error in tsquery` on input as ordinary as
        // a trailing "&", which would surface as a 500 from the search box. It also handles
        // "quoted phrases" and -negation.
        //
        // The call must stay inside the expression tree. EF.Functions methods are translation
        // stubs with no CLR implementation, so hoisting this into a local variable makes EF
        // try to evaluate it client-side, where it throws instead of translating.
        const string config = CardConfiguration.TextSearchConfig;

        return await _db.Cards
            .AsNoTracking()
            // Scoping, not authorization (that ran in the controller): a card is only
            // reachable through a list on this board.
            .Where(c => c.List.BoardId == boardId
                        && EF.Property<NpgsqlTsVector>(c, CardConfiguration.SearchVectorProperty)
                            .Matches(EF.Functions.WebSearchToTsQuery(config, query)))
            // ts_rank over the weighted vector: a title hit (weight A) outranks a body hit
            // (weight B). Id is a tiebreaker, since short cards often rank identically and
            // without it equal-rank results come back in whatever order the plan yields.
            .OrderByDescending(c => EF.Property<NpgsqlTsVector>(c, CardConfiguration.SearchVectorProperty)
                .Rank(EF.Functions.WebSearchToTsQuery(config, query)))
            .ThenBy(c => c.Id)
            .Take(limit)
            .Select(c => new CardSearchHit(
                c.Id, c.ListId, c.List.Name, c.Title, c.Description, c.DueDate, c.AssigneeId))
            .ToListAsync(ct);
    }
}
