using Yanban.Application.Lists;

namespace Yanban.Application.Abstractions;

/// <summary>List (column) operations, always scoped to a board. Authorization is enforced upstream.</summary>
public interface IListService
{
    Task<IReadOnlyList<ListDto>> ListAsync(Guid boardId, CancellationToken ct);
    Task<ListDto> CreateAsync(Guid boardId, CreateListRequest request, CancellationToken ct);
    Task<ListDto> RenameAsync(Guid boardId, Guid listId, RenameListRequest request, CancellationToken ct);
    Task DeleteAsync(Guid boardId, Guid listId, CancellationToken ct);
}
