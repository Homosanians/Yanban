using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Yanban.API.Authorization;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Realtime;

/// <summary>
/// The realtime face of a board. A client connects once and subscribes to the boards it
/// is looking at; the outbox tailer (<see cref="ActivityDispatcher"/>) does all the
/// sending, and the hub only decides who is allowed to listen.
///
/// <para>Subscribing runs the same resource-based authorization as the REST API
/// (<see cref="BoardPermissionRequirement"/>, then BoardAccess), so the live feed cannot
/// become a second, weaker way in. It works over a WebSocket because
/// <c>BoardAuthorizationHandler</c> takes the caller from the authorization context's
/// principal; there is no per-invocation HttpContext to read here.</para>
/// </summary>
[Authorize]
public class BoardHub : Hub<IBoardClient>
{
    private readonly YanbanDbContext _db;
    private readonly IAuthorizationService _authz;
    private readonly BoardSubscriptionRegistry _registry;

    public BoardHub(YanbanDbContext db, IAuthorizationService authz, BoardSubscriptionRegistry registry)
    {
        _db = db;
        _authz = authz;
        _registry = registry;
    }

    public static string GroupFor(Guid boardId) => $"board:{boardId}";

    private Guid UserId => Guid.Parse(Context.User!.FindFirst("sub")!.Value);

    public async Task Subscribe(Guid boardId)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(b => b.Id == boardId, Context.ConnectionAborted)
            ?? throw new HubException("Board not found.");

        var result = await _authz.AuthorizeAsync(Context.User!, board, new BoardPermissionRequirement(BoardPermission.Read));
        if (!result.Succeeded)
            throw new HubException("You do not have permission to view this board.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(boardId), Context.ConnectionAborted);
        _registry.Add(boardId, UserId, Context.ConnectionId);
    }

    public async Task Unsubscribe(Guid boardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(boardId), Context.ConnectionAborted);
        _registry.Remove(boardId, UserId, Context.ConnectionId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR drops the connection from its groups on its own, but knows nothing about
        // our registry; left alone it would leak an entry per dropped connection.
        _registry.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
