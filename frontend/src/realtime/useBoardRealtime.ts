import { useEffect } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { getFreshAccessToken } from "../lib/apiClient";
import type { Activity } from "../types";

/**
 * Keeps a board's cached queries honest while other people change it.
 *
 * The server pushes a notification, never a diff: an event says what kind of thing changed
 * and nothing about its new state. So the only reaction is to drop the affected queries and
 * refetch, which also makes duplicate or out-of-order delivery harmless.
 *
 * The hub lives at /api/hubs/board so it goes through the same origin-preserving proxy as the
 * REST API. That is why this app needs no CORS: there is no cross-origin request to allow.
 */
export function useBoardRealtime(boardId: string | undefined, selfUserId: string | undefined): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!boardId) return;

    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl("/api/hubs/board", {
        // A browser cannot set an Authorization header on a WebSocket handshake, so SignalR
        // puts the token in the query string; the API accepts that only under /hubs.
        //
        // Must hand back a fresh token, not the last one seen: the server closes the socket
        // when the token expires, and the reconnect that follows would otherwise present the
        // same expired token and loop.
        accessTokenFactory: () => getFreshAccessToken(),
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const invalidate = (activity: Activity) => {
      // Ignore our own echo. The actor is in their own board group, so every mutation comes
      // back to whoever made it, and invalidating on that would fight the optimistic update
      // already on screen, making a dragged card visibly snap back and settle.
      if (activity.actorId === selfUserId) return;

      switch (activity.entityType) {
        case "List":
          void queryClient.invalidateQueries({ queryKey: ["boards", boardId, "lists"] });
          break;
        case "Card":
          // Which list the card is in may itself have changed, so invalidate the board's
          // cards wholesale rather than guessing at the one list that moved.
          void queryClient.invalidateQueries({ queryKey: ["boards", boardId] });
          break;
        case "Comment":
        case "Attachment":
          void queryClient.invalidateQueries({
            queryKey: ["boards", boardId, "cards", activity.entityId],
          });
          // The entity id is the comment/attachment, not the card, so the card-scoped key above
          // misses. Fall back to the board subtree; refetching a little extra is the cheap side.
          void queryClient.invalidateQueries({ queryKey: ["boards", boardId] });
          break;
        case "Member":
          void queryClient.invalidateQueries({ queryKey: ["boards", boardId, "members"] });
          break;
        case "Board":
        case "Template":
          void queryClient.invalidateQueries({ queryKey: ["boards", boardId] });
          break;
      }

      // The feed itself always changed.
      void queryClient.invalidateQueries({ queryKey: ["boards", boardId, "activity"] });
    };

    connection.on("ActivityOccurred", invalidate);

    // The server evicts a removed member's live connections. Without this the board would
    // simply go quiet and look fine while being stale and wrong.
    connection.on("BoardAccessRevoked", () => {
      void queryClient.invalidateQueries({ queryKey: ["boards"] });
    });

    let cancelled = false;
    void connection
      .start()
      .then(() => {
        if (cancelled) return;
        return connection.invoke("Subscribe", boardId);
      })
      .catch(() => {
        // A board we cannot watch is not a broken page: the REST calls have already decided
        // whether we can see it. Realtime is an enhancement, so its failure stays silent.
      });

    // Re-subscribe after a reconnect: the server's group membership did not survive the drop.
    connection.onreconnected(() => {
      void connection.invoke("Subscribe", boardId).catch(() => {});
    });

    return () => {
      cancelled = true;
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [boardId, selfUserId, queryClient]);
}
