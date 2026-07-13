import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { contentKeys, searchCards } from "../api/board-content";

interface Props {
  boardId: string;
  onOpenCard: (cardId: string) => void;
}

export function SearchBox({ boardId, onOpenCard }: Props) {
  const [q, setQ] = useState("");
  const [debounced, setDebounced] = useState("");

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

  return (
    <div className="search">
      <input
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Search cards"
        aria-label="Search cards"
      />
      {debounced && (
        <div className="results">
          {results.isLoading && <p className="muted">Searching...</p>}
          {results.data?.length === 0 && <p className="muted">No matches.</p>}
          {results.data?.map((hit) => (
            <button
              key={hit.id}
              className="result"
              onClick={() => {
                onOpenCard(hit.id);
                setQ("");
              }}
            >
              <span className="result-title">{hit.title}</span>
              <span className="muted">{hit.listName}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
