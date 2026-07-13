import { useInfiniteQuery } from "@tanstack/react-query";
import { contentKeys, listActivity } from "../api/board-content";

interface Props {
  boardId: string;
  onClose: () => void;
}

const PAGE_SIZE = 20;

export function ActivityFeed({ boardId, onClose }: Props) {
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
      <header className="drawer-head">
        <h2>Activity</h2>
        <button className="ghost" onClick={onClose}>Close</button>
      </header>

      {feed.isLoading && <p>Loading...</p>}
      {feed.isError && <p className="error">{(feed.error as Error).message}</p>}

      <ul className="plain">
        {entries.map((a) => (
          <li key={a.sequence} className="activity">
            <div className="muted">{new Date(a.createdAt).toLocaleString()}</div>
            <div>
              <strong>{a.actorDisplayName}</strong> {a.summary ?? `${a.action} ${a.entityType}`}
            </div>
          </li>
        ))}
      </ul>

      {feed.hasNextPage && (
        <button className="ghost" onClick={() => void feed.fetchNextPage()} disabled={feed.isFetchingNextPage}>
          {feed.isFetchingNextPage ? "Loading..." : "Load older"}
        </button>
      )}
      {entries.length === 0 && !feed.isLoading && <p className="muted">Nothing has happened yet.</p>}
    </aside>
  );
}
