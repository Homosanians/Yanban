import { X } from "lucide-react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { contentKeys, listActivity } from "../api/board-content";
import type { BoardMember } from "../types";
import { Avatar } from "./Avatar";

interface Props {
  boardId: string;
  /** Only used to find the actor's email, which is what the avatar's colour is keyed on. */
  members: BoardMember[];
  onClose: () => void;
}

const PAGE_SIZE = 20;

export function ActivityFeed({ boardId, members, onClose }: Props) {
  // Keyset paging, mirroring the API: the next page asks for entries *before* the oldest
  // sequence already held. No offsets — an event arriving mid-scroll must not shift the window
  // and make a row appear twice.
  const feed = useInfiniteQuery({
    queryKey: contentKeys.activity(boardId),
    queryFn: ({ pageParam }) => listActivity(boardId, pageParam),
    initialPageParam: undefined as number | undefined,
    getNextPageParam: (lastPage) =>
      lastPage.length < PAGE_SIZE ? undefined : lastPage.at(-1)?.sequence,
  });

  const entries = feed.data?.pages.flat() ?? [];

  return (
    <aside className="panel">
      <header className="panel-head">
        <h2>Activity</h2>
        <button className="icon-btn" aria-label="Close activity" title="Close" onClick={onClose}>
          <X size={16} />
        </button>
      </header>

      <div className="panel-body">
        {feed.isLoading && <p className="muted">Loading…</p>}
        {feed.isError && <p className="error">{(feed.error as Error).message}</p>}

        <ul className="plain">
          {entries.map((a) => {
            // The feed carries a display name but not an email, and the avatar's colour is a
            // function of the email — so an actor who has since left the board gets no avatar
            // rather than a differently-coloured one.
            const actor = members.find((m) => m.userId === a.actorId);
            return (
              <li key={a.sequence} className="event">
                {actor && <Avatar email={actor.email} name={actor.displayName} size="sm" />}
                <div className="what">
                  <span>
                    <strong>{a.actorDisplayName}</strong>{" "}
                    {a.summary ?? `${a.action.toLowerCase()} a ${a.entityType.toLowerCase()}`}
                  </span>
                  <div className="when">{new Date(a.createdAt).toLocaleString()}</div>
                </div>
              </li>
            );
          })}
        </ul>

        {feed.hasNextPage && (
          <button
            className="secondary"
            onClick={() => void feed.fetchNextPage()}
            disabled={feed.isFetchingNextPage}
          >
            {feed.isFetchingNextPage ? "Loading…" : "Load older"}
          </button>
        )}
        {entries.length === 0 && !feed.isLoading && <p className="empty">Nothing has happened yet.</p>}
      </div>
    </aside>
  );
}
