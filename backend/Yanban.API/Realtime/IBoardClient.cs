using Yanban.Application.Activities;

namespace Yanban.API.Realtime;

/// <summary>
/// What the server can call on a connected board client. Typing the hub with this
/// (<c>Hub&lt;IBoardClient&gt;</c>) turns method names into compile-time contracts
/// rather than magic strings on both ends.
/// </summary>
public interface IBoardClient
{
    /// <summary>
    /// Something changed on a board you are watching. This is a notification, not a diff:
    /// it names the entity that changed and says nothing about its new state. Clients react
    /// by invalidating and refetching, which makes at-least-once, possibly out-of-order
    /// delivery harmless.
    /// </summary>
    Task ActivityOccurred(ActivityDto activity);

    /// <summary>Your access to this board is gone; the server has stopped sending you its events.</summary>
    Task BoardAccessRevoked(Guid boardId);
}
