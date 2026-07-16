import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { CornerDownLeft, Search } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { contentKeys, searchCards } from "../api/board-content";

interface Props {
  boardId: string;
  onPick: (cardId: string) => void;
  onClose: () => void;
}

/**
 * Cmd/Ctrl K over the board's full-text search. Search is board-scoped on the server, so this is
 * too: there is no such thing as a global query here.
 */
export function CommandPalette({ boardId, onPick, onClose }: Props) {
  const [q, setQ] = useState("");
  const [debounced, setDebounced] = useState("");
  const [active, setActive] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  // One request per pause in typing, not per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(timer);
  }, [q]);

  const results = useQuery({
    queryKey: contentKeys.search(boardId, debounced),
    queryFn: () => searchCards(boardId, debounced),
    // A blank query is a 400 from the API by design (it will not dump the board), so don't ask.
    enabled: debounced.length > 0,
  });

  const hits = results.data ?? [];

  // A stale index would point Enter at whichever card happens to be sitting in that slot now.
  useEffect(() => setActive(0), [debounced]);

  // Keep the highlighted row in view when arrowing past the fold.
  useEffect(() => {
    listRef.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [active]);

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((i) => (hits.length ? (i + 1) % hits.length : 0));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((i) => (hits.length ? (i - 1 + hits.length) % hits.length : 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      const hit = hits[active];
      if (hit) onPick(hit.id);
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    }
  };

  return (
    <>
      <div className="overlay" onClick={onClose} />
      <div className="palette-wrap">
        <div className="palette" role="dialog" aria-modal="true" aria-label="Search cards">
          <div className="palette-input">
            <Search size={17} />
            <input
              autoFocus
              value={q}
              onChange={(e) => setQ(e.target.value)}
              onKeyDown={onKeyDown}
              placeholder="Search cards on this board"
              aria-label="Search cards"
            />
          </div>

          <div className="palette-results" ref={listRef} role="listbox">
            {hits.map((hit, i) => (
              <button
                key={hit.id}
                className="palette-item"
                role="option"
                aria-selected={i === active}
                // Hover moves the selection, so the mouse and the keyboard never disagree about
                // which row Enter would open.
                onMouseEnter={() => setActive(i)}
                onClick={() => onPick(hit.id)}
              >
                <span className="truncate">{hit.title}</span>
                <span className="where">{hit.listName}</span>
              </button>
            ))}

            {debounced && !results.isLoading && hits.length === 0 && (
              <p className="empty">No cards match “{debounced}”.</p>
            )}
            {!debounced && <p className="empty">Type to search titles and descriptions.</p>}
          </div>

          <div className="palette-foot">
            <span><kbd>↑</kbd><kbd>↓</kbd> navigate</span>
            <span><kbd><CornerDownLeft size={10} /></kbd> open</span>
            <span><kbd>Esc</kbd> close</span>
          </div>
        </div>
      </div>
    </>
  );
}
