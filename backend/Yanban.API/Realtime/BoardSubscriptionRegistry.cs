using System.Collections.Concurrent;

namespace Yanban.API.Realtime;

/// <summary>
/// Who is watching which board, from this instance. SignalR groups are write-only — you
/// can add and remove a connection but not ask who is in one — so a board's live
/// subscribers cannot be revoked without tracking them ourselves.
///
/// <para>This exists for exactly one reason: when a member is removed from a board, their
/// open connections must stop receiving its events. Group membership would otherwise
/// outlive their authorization (until the connection drops), which is a real hole in a
/// codebase that goes as far as TokenVersion to revoke access promptly.</para>
///
/// <para>Per-instance and in-memory by design: it only ever names connections held by
/// <i>this</i> process, which are the only ones this process can evict. Every instance
/// tails the same outbox, so each independently evicts its own (ADR-11).</para>
/// </summary>
public sealed class BoardSubscriptionRegistry
{
    private readonly ConcurrentDictionary<(Guid BoardId, Guid UserId), ConcurrentDictionary<string, byte>> _subscriptions = new();

    public void Add(Guid boardId, Guid userId, string connectionId) =>
        _subscriptions.GetOrAdd((boardId, userId), _ => new ConcurrentDictionary<string, byte>()).TryAdd(connectionId, 0);

    public IReadOnlyCollection<string> ConnectionsFor(Guid boardId, Guid userId) =>
        _subscriptions.TryGetValue((boardId, userId), out var connections)
            ? connections.Keys.ToArray()
            : Array.Empty<string>();

    public void Remove(Guid boardId, Guid userId, string connectionId)
    {
        if (!_subscriptions.TryGetValue((boardId, userId), out var connections))
            return;

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
            _subscriptions.TryRemove((boardId, userId), out _);
    }

    public void RemoveAll(Guid boardId, Guid userId) => _subscriptions.TryRemove((boardId, userId), out _);

    /// <summary>
    /// Forgets a dropped connection wherever it appears. Linear in the number of
    /// (board, user) pairs on this instance — cheap at this scale, and the alternative
    /// (a second connection→boards index) is more state to keep consistent than it saves.
    /// </summary>
    public void RemoveConnection(string connectionId)
    {
        foreach (var (key, connections) in _subscriptions)
        {
            if (!connections.TryRemove(connectionId, out _))
                continue;

            if (connections.IsEmpty)
                _subscriptions.TryRemove(key, out _);
        }
    }
}
