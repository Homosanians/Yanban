import { useEffect, useState } from "react";
import { ArrowRight, Search, X } from "lucide-react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { contentKeys, listActivity } from "../api/board-content";
import type { ActivityFilters, BoardMember } from "../types";
import { Avatar } from "./Avatar";
import { Dropdown } from "./Dropdown";

interface Props {
  boardId: string;
  /** Used for the actor avatars, and to populate the "who" filter. */
  members: BoardMember[];
  onClose: () => void;
}

const PAGE_SIZE = 20;

const ENTITY_TYPES = ["Board", "List", "Card", "Comment", "Attachment", "Member", "Template"];
const ACTIONS = ["Created", "Updated", "Deleted", "Moved", "Assigned"];

export function ActivityFeed({ boardId, members, onClose }: Props) {
  const [q, setQ] = useState("");
  const [debounced, setDebounced] = useState("");
  const [actorId, setActorId] = useState("");
  const [action, setAction] = useState("");
  const [entityType, setEntityType] = useState("");

  // One request per pause in typing, not per keystroke; the same 250ms the command palette uses.
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(timer);
  }, [q]);

  const filters: ActivityFilters = {
    q: debounced || undefined,
    actorId: actorId || undefined,
    action: action || undefined,
    entityType: entityType || undefined,
  };

  // Keyset paging, mirroring the API: the next page asks for entries before the oldest sequence
  // already held. The filters are part of the query key, so changing one starts a fresh feed rather
  // than appending different results to the old one.
  const feed = useInfiniteQuery({
    queryKey: [...contentKeys.activity(boardId), filters],
    queryFn: ({ pageParam }) => listActivity(boardId, pageParam, filters),
    initialPageParam: undefined as number | undefined,
    getNextPageParam: (lastPage) =>
      lastPage.length < PAGE_SIZE ? undefined : lastPage.at(-1)?.sequence,
  });

  const entries = feed.data?.pages.flat() ?? [];
  const filtered = Boolean(debounced || actorId || action || entityType);

  const clear = () => {
    setQ("");
    setActorId("");
    setAction("");
    setEntityType("");
  };

  return (
    <aside className="panel">
      <header className="panel-head">
        <h2>Activity</h2>
        <button className="icon-btn" aria-label="Close activity" title="Close" onClick={onClose}>
          <X size={16} />
        </button>
      </header>

      <div className="panel-filters">
        <div className="field">
          <Search size={14} />
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Search the audit log"
            aria-label="Search the audit log"
          />
        </div>

        <div className="row">
          <Dropdown
            value={actorId}
            ariaLabel="Filter by member"
            onChange={setActorId}
            options={[{ value: "", label: "Anyone" }, ...members.map((m) => ({ value: m.userId, label: m.displayName }))]}
          />
          <Dropdown
            value={action}
            ariaLabel="Filter by action"
            onChange={setAction}
            options={[{ value: "", label: "Any action" }, ...ACTIONS.map((a) => ({ value: a, label: a }))]}
          />
          <Dropdown
            value={entityType}
            ariaLabel="Filter by type"
            onChange={setEntityType}
            options={[{ value: "", label: "Anything" }, ...ENTITY_TYPES.map((t) => ({ value: t, label: t }))]}
          />
        </div>

        {filtered && (
          <button className="link" onClick={clear}>Clear filters</button>
        )}
      </div>

      <div className="panel-body">
        {feed.isLoading && <p className="muted">Loading…</p>}
        {feed.isError && <p className="error">{(feed.error as Error).message}</p>}

        <ul className="plain">
          {entries.map((a) => {
            // The feed carries a display name but not an email, and the avatar's colour is a
            // function of the email, so an actor who has since left the board gets no avatar
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

                  {/* A rename is the one event where "what it was" is the point. */}
                  {a.oldValue && a.newValue && (
                    <div className="diff">
                      <span className="was">{a.oldValue}</span>
                      <ArrowRight size={12} />
                      <span className="now">{a.newValue}</span>
                    </div>
                  )}

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

        {entries.length === 0 && !feed.isLoading && (
          <p className="empty">{filtered ? "Nothing matches those filters." : "Nothing has happened yet."}</p>
        )}
      </div>
    </aside>
  );
}
