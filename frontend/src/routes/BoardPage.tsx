import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  DndContext,
  DragOverlay,
  MeasuringStrategy,
  PointerSensor,
  pointerWithin,
  rectIntersection,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import type { CollisionDetection, DragEndEvent, DragStartEvent } from "@dnd-kit/core";
import { ArrowLeft, History, LayoutTemplate, LogOut, Plus, Search, Users } from "lucide-react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { boardKeys, getBoard, listMembers } from "../api/boards";
import { assignCard, contentKeys, createList, listCards, listLists, moveCard } from "../api/board-content";
import { useAuth } from "../auth/useAuth";
import { useBoardRealtime } from "../realtime/useBoardRealtime";
import { canWrite } from "../types";
import type { Card } from "../types";
import { ListColumn } from "../components/ListColumn";
import { CardOverlay } from "../components/CardTile";
import { Avatar } from "../components/Avatar";
import { CardDetail } from "../components/CardDetail";
import { MembersPanel } from "../components/MembersPanel";
import { ActivityFeed } from "../components/ActivityFeed";
import { TemplatesPanel } from "../components/TemplatesPanel";
import { CommandPalette } from "../components/CommandPalette";
import { ThemeToggle } from "../components/ThemeToggle";

type SidePanel = "members" | "activity" | "templates" | null;

/**
 * Pointer-first collision detection — and the reason a card no longer flickers between two slots
 * when you drag it to the bottom of its own column.
 *
 * The rect-based detectors (closestCorners and friends) answer "which droppable is nearest to the
 * *dragged element's* box". That box is being moved by the sort strategy at the same time as the
 * strategy is reacting to the answer, so near a boundary the two chase each other and the card
 * oscillates. The pointer does not move because of anything we render, which breaks the loop.
 *
 * rectIntersection is only a fallback for when the pointer has left every droppable (dragged out
 * past the edge of the board), where there is no loop to worry about.
 */
const collisionDetection: CollisionDetection = (args) => {
  const byPointer = pointerWithin(args);
  return byPointer.length > 0 ? byPointer : rectIntersection(args);
};

/** Space must not fire an assign while someone is typing into a card title or a comment. */
const isTyping = (target: EventTarget | null): boolean => {
  const el = target as HTMLElement | null;
  if (!el) return false;
  return (
    el.isContentEditable ||
    el.tagName === "INPUT" ||
    el.tagName === "TEXTAREA" ||
    el.tagName === "SELECT"
  );
};

export function BoardPage() {
  const { boardId = "" } = useParams();
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();

  const [openCardId, setOpenCardId] = useState<string | null>(null);
  const [panel, setPanel] = useState<SidePanel>(null);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [addingList, setAddingList] = useState(false);
  const [newListName, setNewListName] = useState("");
  const [dragError, setDragError] = useState<string | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);
  const [hoveredCardId, setHoveredCardId] = useState<string | null>(null);

  // Live updates from everyone else's mutations (ADR-11). Our own echo is filtered out inside.
  useBoardRealtime(boardId, user?.id);

  const board = useQuery({ queryKey: boardKeys.one(boardId), queryFn: () => getBoard(boardId) });
  const lists = useQuery({ queryKey: contentKeys.lists(boardId), queryFn: () => listLists(boardId) });
  const members = useQuery({ queryKey: boardKeys.members(boardId), queryFn: () => listMembers(boardId) });

  // One cards query per list — the API has no board-wide card read, and per-list keys are what
  // let a realtime invalidation land on just the lists that changed.
  const cardQueries = useQueries({
    queries: (lists.data ?? []).map((list) => ({
      queryKey: contentKeys.cards(boardId, list.id),
      queryFn: () => listCards(boardId, list.id),
    })),
  });

  const cardsByList: Record<string, Card[]> = {};
  (lists.data ?? []).forEach((list, i) => {
    cardsByList[list.id] = cardQueries[i]?.data ?? [];
  });

  const writable = board.data ? canWrite(board.data.role) && !board.data.archived : false;

  const addList = useMutation({
    mutationFn: (name: string) => createList(boardId, name),
    onSuccess: () => {
      setNewListName("");
      setAddingList(false);
      void queryClient.invalidateQueries({ queryKey: contentKeys.lists(boardId) });
    },
  });

  // --- ⌘K -----------------------------------------------------------------
  // Deliberately fires even when an input has focus: a command palette that you cannot open
  // from the box you are typing in is not a command palette.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPaletteOpen(true);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // --- Space: take the card, or hand it back ------------------------------
  const assign = useMutation({
    mutationFn: (v: { cardId: string; listId: string; assigneeId: string | null }) =>
      assignCard(boardId, v.cardId, v.assigneeId),
    onMutate: async (v) => {
      // Show it immediately — a keyboard shortcut that waits for a round-trip feels broken.
      const key = contentKeys.cards(boardId, v.listId);
      await queryClient.cancelQueries({ queryKey: key });
      const snapshot = queryClient.getQueryData<Card[]>(key);
      queryClient.setQueryData<Card[]>(key, (old) =>
        (old ?? []).map((c) => (c.id === v.cardId ? { ...c, assigneeId: v.assigneeId } : c)),
      );
      return { key, snapshot };
    },
    onError: (err, _v, ctx) => {
      if (ctx) queryClient.setQueryData(ctx.key, ctx.snapshot);
      setDragError(err instanceof Error ? err.message : "Could not assign the card.");
    },
    onSettled: (_d, _e, _v, ctx) => {
      if (ctx) void queryClient.invalidateQueries({ queryKey: ctx.key });
    },
  });

  // `cardsByList` is rebuilt on every render, so an effect that depended on it directly would
  // tear down and re-bind the window listener constantly. Park the moving parts in a ref that is
  // refreshed after each render, and subscribe exactly once.
  const shortcut = useRef({ cardsByList, hoveredCardId, openCardId, paletteOpen, writable, user, assign });
  useEffect(() => {
    shortcut.current = { cardsByList, hoveredCardId, openCardId, paletteOpen, writable, user, assign };
  });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.code !== "Space") return;
      // A modified Space is somebody else's shortcut, not ours.
      if (e.metaKey || e.ctrlKey || e.altKey || e.shiftKey) return;
      // Otherwise Space would fire an assign while someone is typing a card title.
      if (isTyping(e.target)) return;

      const s = shortcut.current;
      // Anything modal owns the keyboard while it is up.
      if (s.openCardId || s.paletteOpen) return;
      if (!s.writable || !s.hoveredCardId || !s.user) return;

      const listId = Object.keys(s.cardsByList).find((id) =>
        s.cardsByList[id].some((c) => c.id === s.hoveredCardId),
      );
      const card = listId ? s.cardsByList[listId].find((c) => c.id === s.hoveredCardId) : undefined;
      if (!listId || !card) return;

      // Or the page scrolls out from under the board.
      e.preventDefault();

      // Toggle: already mine? hand it back. Someone else's? take it over.
      const next = card.assigneeId === s.user.id ? null : s.user.id;
      s.assign.mutate({ cardId: card.id, listId, assigneeId: next });
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // --- drag ---------------------------------------------------------------

  // A click must not be swallowed as a drag: without a distance threshold, opening a card
  // would start a 0-pixel drag instead.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const onDragStart = (e: DragStartEvent) => setDragging(String(e.active.id));

  const onDragEnd = async (e: DragEndEvent) => {
    setDragging(null);
    const { active, over } = e;
    if (!over) return;

    const cardId = String(active.id);
    const overId = String(over.id);

    const sourceListId = Object.keys(cardsByList).find((id) =>
      cardsByList[id].some((c) => c.id === cardId),
    );
    if (!sourceListId) return;

    // `over` is either another card or an empty list's drop area.
    const overIsList = Boolean(cardsByList[overId]);
    const targetListId = overIsList
      ? overId
      : Object.keys(cardsByList).find((id) => cardsByList[id].some((c) => c.id === overId));
    if (!targetListId) return;

    // The API's `position` is an index among the target list's *other* cards — so the index is
    // read from the list *including* the dragged card, which is what makes it agree with the
    // preview dnd-kit is drawing.
    //
    // This is the off-by-one that made a card dropped at the bottom of its own column jump back
    // up one slot. The sort strategy previews an arrayMove: drag Alpha onto Echo and everything
    // between them shifts up, so Alpha lands *after* Echo. Reading the index out of the list with
    // Alpha already removed gives Echo's index as 3, and the card is inserted *before* Echo —
    // one short of where the user just watched it settle. Reading it from the full list gives 4,
    // which is exactly arrayMove's answer.
    //
    // A cross-list move needs no such care: the dragged card is not in the target list, so the
    // two indices coincide, and inserting before the hovered card is what you want anyway.
    const target = cardsByList[targetListId];
    const withoutDragged = target.filter((c) => c.id !== cardId);
    const overIndex = overIsList ? withoutDragged.length : target.findIndex((c) => c.id === overId);
    const position = overIndex < 0 ? withoutDragged.length : overIndex;

    const sourceIndex = cardsByList[sourceListId].findIndex((c) => c.id === cardId);
    if (sourceListId === targetListId && sourceIndex === position) return;

    const card = cardsByList[sourceListId][sourceIndex];
    const sourceKey = contentKeys.cards(boardId, sourceListId);
    const targetKey = contentKeys.cards(boardId, targetListId);

    // Snapshot for rollback, then move it on screen immediately: a card that lags behind the
    // pointer by a round-trip feels broken even when it is working.
    const sourceSnapshot = queryClient.getQueryData<Card[]>(sourceKey);
    const targetSnapshot = queryClient.getQueryData<Card[]>(targetKey);

    const remaining = (sourceSnapshot ?? []).filter((c) => c.id !== cardId);
    if (sourceListId === targetListId) {
      const reordered = [...remaining];
      reordered.splice(position, 0, { ...card, listId: targetListId });
      queryClient.setQueryData(sourceKey, reordered);
    } else {
      const inserted = [...(targetSnapshot ?? [])].filter((c) => c.id !== cardId);
      inserted.splice(position, 0, { ...card, listId: targetListId });
      queryClient.setQueryData(sourceKey, remaining);
      queryClient.setQueryData(targetKey, inserted);
    }

    setDragError(null);
    try {
      await moveCard(boardId, cardId, targetListId, position);
    } catch (err) {
      // Put it back where it was. The server refused (an archived board, a lost membership),
      // and leaving the optimistic position on screen would be a lie.
      queryClient.setQueryData(sourceKey, sourceSnapshot);
      queryClient.setQueryData(targetKey, targetSnapshot);
      setDragError(err instanceof Error ? err.message : "Could not move the card.");
    } finally {
      // Ranks are the server's to assign — refetch rather than trust our guess at the order.
      void queryClient.invalidateQueries({ queryKey: sourceKey });
      if (sourceListId !== targetListId) void queryClient.invalidateQueries({ queryKey: targetKey });
    }
  };

  const openFromPalette = useCallback((cardId: string) => {
    setPaletteOpen(false);
    setOpenCardId(cardId);
  }, []);

  const togglePanel = (next: Exclude<SidePanel, null>) =>
    setPanel((current) => (current === next ? null : next));

  if (board.isLoading) return <p className="pad muted">Loading board…</p>;
  if (board.isError) {
    // A non-member gets a real message rather than a blank board.
    return (
      <div className="pad stack">
        <p className="error">{(board.error as Error).message}</p>
        <Link to="/">Back to boards</Link>
      </div>
    );
  }

  const activeCard = dragging
    ? Object.values(cardsByList).flat().find((c) => c.id === dragging) ?? null
    : null;

  return (
    <div className="page">
      <header className="topbar">
        <div className="brand">
          <Link to="/" className="icon-btn" aria-label="Back to boards" title="Back to boards">
            <ArrowLeft size={18} />
          </Link>
          <h1>{board.data?.name}</h1>
          {board.data?.archived && <span className="badge">Archived · read-only</span>}
        </div>

        <div className="spacer" />

        <div className="tools">
          <button className="search-trigger" onClick={() => setPaletteOpen(true)}>
            <Search size={15} />
            <span>Search</span>
            <kbd>{navigator.platform.startsWith("Mac") ? "⌘" : "Ctrl"} K</kbd>
          </button>

          <span className="divider" />

          <button
            className={panel === "members" ? "icon-btn on" : "icon-btn"}
            aria-label="Members" title="Members"
            onClick={() => togglePanel("members")}
          >
            <Users size={17} />
          </button>
          <button
            className={panel === "templates" ? "icon-btn on" : "icon-btn"}
            aria-label="Card templates" title="Card templates"
            onClick={() => togglePanel("templates")}
          >
            <LayoutTemplate size={17} />
          </button>
          <button
            className={panel === "activity" ? "icon-btn on" : "icon-btn"}
            aria-label="Activity" title="Activity"
            onClick={() => togglePanel("activity")}
          >
            <History size={17} />
          </button>

          <span className="divider" />

          <ThemeToggle />
          {user && <Avatar email={user.email} name={user.displayName} />}
          <button className="icon-btn" aria-label="Log out" title="Log out" onClick={() => void logout()}>
            <LogOut size={17} />
          </button>
        </div>
      </header>

      <div className="board-body">
        <DndContext
          sensors={sensors}
          collisionDetection={collisionDetection}
          // Cards appear, vanish and reflow under the pointer; stale rects would send a drop to
          // the wrong slot.
          measuring={{ droppable: { strategy: MeasuringStrategy.Always } }}
          onDragStart={onDragStart}
          onDragEnd={(e) => void onDragEnd(e)}
          onDragCancel={() => setDragging(null)}
        >
          <div className="columns">
            {lists.data?.map((list) => (
              <ListColumn
                key={list.id}
                boardId={boardId}
                list={list}
                cards={cardsByList[list.id] ?? []}
                members={members.data ?? []}
                writable={writable}
                onOpenCard={setOpenCardId}
                onHoverCard={setHoveredCardId}
              />
            ))}

            {writable && (
              <div className="add-column">
                {addingList ? (
                  <form
                    onSubmit={(e) => {
                      e.preventDefault();
                      if (newListName.trim()) addList.mutate(newListName.trim());
                    }}
                  >
                    <input
                      value={newListName}
                      onChange={(e) => setNewListName(e.target.value)}
                      onKeyDown={(e) => e.key === "Escape" && setAddingList(false)}
                      placeholder="List name"
                      maxLength={200}
                      autoFocus
                    />
                    <div className="row">
                      <button type="submit" disabled={!newListName.trim() || addList.isPending}>
                        Add list
                      </button>
                      <button type="button" className="ghost" onClick={() => setAddingList(false)}>
                        Cancel
                      </button>
                    </div>
                  </form>
                ) : (
                  <button className="add-trigger" onClick={() => setAddingList(true)}>
                    <Plus size={15} />
                    Add a list
                  </button>
                )}
              </div>
            )}
          </div>

          {/* The only thing that follows the cursor. Rendering the drag as a separate layer is
              what lets the card in the column stay put and behave as a placeholder. */}
          <DragOverlay dropAnimation={null}>
            {activeCard && <CardOverlay card={activeCard} members={members.data ?? []} />}
          </DragOverlay>
        </DndContext>

        {panel === "members" && (
          <MembersPanel boardId={boardId} role={board.data!.role} onClose={() => setPanel(null)} />
        )}
        {panel === "activity" && (
          <ActivityFeed boardId={boardId} members={members.data ?? []} onClose={() => setPanel(null)} />
        )}
        {panel === "templates" && (
          <TemplatesPanel boardId={boardId} writable={writable} onClose={() => setPanel(null)} />
        )}
      </div>

      {paletteOpen && (
        <CommandPalette boardId={boardId} onPick={openFromPalette} onClose={() => setPaletteOpen(false)} />
      )}

      {openCardId && (
        <CardDetail
          boardId={boardId}
          cardId={openCardId}
          members={members.data ?? []}
          writable={writable}
          selfId={user?.id ?? ""}
          onClose={() => setOpenCardId(null)}
        />
      )}

      {dragError && (
        <p className="toast" onClick={() => setDragError(null)}>{dragError}</p>
      )}
    </div>
  );
}
