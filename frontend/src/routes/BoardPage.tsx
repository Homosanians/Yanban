import { type FormEvent, useCallback, useEffect, useRef, useState } from "react";
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
import { ArrowLeft, History, LayoutTemplate, LogOut, Pencil, Plus, Search, Settings, Users } from "lucide-react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { boardKeys, getBoard, listMembers, renameBoard } from "../api/boards";
import { assignCard, contentKeys, createList, listCards, listLists, moveCard } from "../api/board-content";
import { ApiError } from "../lib/apiClient";
import { useAuth } from "../auth/useAuth";
import { useBoardRealtime } from "../realtime/useBoardRealtime";
import { canAdmin, canWrite } from "../types";
import type { Card } from "../types";
import { ListColumn } from "../components/ListColumn";
import { CardOverlay } from "../components/CardTile";
import { Avatar } from "../components/Avatar";
import { CardDetail } from "../components/CardDetail";
import { MembersPanel } from "../components/MembersPanel";
import { ActivityFeed } from "../components/ActivityFeed";
import { TemplatesPanel } from "../components/TemplatesPanel";
import { CommandPalette } from "../components/CommandPalette";
import { BoardSettingsPanel } from "../components/BoardSettingsPanel";
import { ThemeToggle } from "../components/ThemeToggle";
import { useToast } from "../toast/useToast";

type SidePanel = "members" | "activity" | "templates" | "settings" | null;

/**
 * Pointer-first collision detection, which keeps a card from flickering between two slots when
 * dragged to the bottom of its own column.
 *
 * The rect-based detectors (closestCorners and friends) answer "which droppable is nearest to the
 * dragged element's box". That box is moved by the sort strategy at the same time as the strategy
 * reacts to the answer, so near a boundary the two chase each other and the card oscillates. The
 * pointer does not move because of anything we render, which breaks the loop.
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
  // One toast mechanism for the whole app.
  const { show } = useToast();

  const [openCardId, setOpenCardId] = useState<string | null>(null);
  const [panel, setPanel] = useState<SidePanel>(null);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [addingList, setAddingList] = useState(false);
  const [newListName, setNewListName] = useState("");
  const [dragging, setDragging] = useState<string | null>(null);
  const [hoveredCardId, setHoveredCardId] = useState<string | null>(null);
  const [renamingBoard, setRenamingBoard] = useState(false);
  const [boardName, setBoardName] = useState("");

  // Live updates from everyone else's mutations. Our own echo is filtered out inside.
  useBoardRealtime(boardId, user?.id);

  const board = useQuery({ queryKey: boardKeys.one(boardId), queryFn: () => getBoard(boardId) });
  const lists = useQuery({ queryKey: contentKeys.lists(boardId), queryFn: () => listLists(boardId) });
  const members = useQuery({ queryKey: boardKeys.members(boardId), queryFn: () => listMembers(boardId) });

  // One cards query per list: the API has no board-wide card read, and per-list keys let a
  // realtime invalidation land on just the lists that changed.
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

  // Renaming is a Manage action, so it is gated on Admin rather than on `writable`: an admin may
  // still rename an archived board, the same way they may unarchive or delete it.
  const canRenameBoard = board.data ? canAdmin(board.data.role) : false;

  const renameBoardMut = useMutation({
    mutationFn: (name: string) => renameBoard(boardId, name),
    onSuccess: (updated) => {
      setRenamingBoard(false);
      // Update the header now, and refetch only the boards list (its key is exactly ["boards"], so
      // `exact` keeps this from invalidating every card and list query nested under it).
      queryClient.setQueryData(boardKeys.one(boardId), updated);
      void queryClient.invalidateQueries({ queryKey: boardKeys.all, exact: true });
    },
    onError: (err) => show(err instanceof Error ? err.message : "Could not rename the board."),
  });

  const submitBoardRename = (e: FormEvent) => {
    e.preventDefault();
    const next = boardName.trim();
    // A no-op rename is just a cancel: don't spend a round-trip to write the same name back.
    if (next && next !== board.data?.name) renameBoardMut.mutate(next);
    else setRenamingBoard(false);
  };

  // --- Cmd/Ctrl K ---------------------------------------------------------
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
      // Show it immediately: a keyboard shortcut that waits for a round-trip feels broken.
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
      show(err instanceof Error ? err.message : "Could not assign the card.");
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

      // Stop Space from scrolling the page.
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

  // Auto-scroll a tall column while dragging. dnd-kit's built-in auto-scroll ramps only against the
  // viewport edge, never a nested scroll container's own edge, so cards below the fold of an
  // overflowing column are otherwise unreachable mid-drag. This tracks the pointer and scrolls
  // whichever column's card list it is hovering when it nears that list's top or bottom, including
  // when it strays just past the bottom onto the "Add a card" strip.
  const pointer = useRef({ x: 0, y: 0 });
  const autoScrollRAF = useRef<number | null>(null);

  const runAutoScroll = useCallback(() => {
    const { x, y } = pointer.current;
    const column = document
      .elementsFromPoint(x, y)
      .map((el) => el.closest(".column"))
      .find(Boolean);
    const list = column?.querySelector<HTMLElement>(".cards");
    if (list) {
      const rect = list.getBoundingClientRect();
      const edge = 72; // px hot band at each end
      const speed = 15; // px per frame at full tilt
      if (y < rect.top + edge) {
        list.scrollTop -= speed * Math.min(1, (rect.top + edge - y) / edge);
      } else if (y > rect.bottom - edge) {
        list.scrollTop += speed * Math.min(1, (y - (rect.bottom - edge)) / edge);
      }
    }
    autoScrollRAF.current = requestAnimationFrame(runAutoScroll);
  }, []);

  const trackPointer = useCallback((e: PointerEvent) => {
    pointer.current = { x: e.clientX, y: e.clientY };
  }, []);

  const stopAutoScroll = useCallback(() => {
    window.removeEventListener("pointermove", trackPointer);
    if (autoScrollRAF.current !== null) cancelAnimationFrame(autoScrollRAF.current);
    autoScrollRAF.current = null;
  }, [trackPointer]);

  // Never leave the rAF loop or the listener running if the component unmounts mid-drag.
  useEffect(() => stopAutoScroll, [stopAutoScroll]);

  const onDragStart = (e: DragStartEvent) => {
    setDragging(String(e.active.id));
    if (e.activatorEvent instanceof PointerEvent) {
      pointer.current = { x: e.activatorEvent.clientX, y: e.activatorEvent.clientY };
    }
    window.addEventListener("pointermove", trackPointer);
    autoScrollRAF.current = requestAnimationFrame(runAutoScroll);
  };

  const onDragCancel = () => {
    setDragging(null);
    stopAutoScroll();
  };

  const onDragEnd = async (e: DragEndEvent) => {
    setDragging(null);
    stopAutoScroll();
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

    // For a same-list move the index must be read from the list including the dragged card, so it
    // matches the arrayMove preview dnd-kit is drawing. Reading it with the dragged card removed
    // lands the card one slot short of where the user watched it settle.
    //
    // A cross-list move needs no such care: the dragged card is not in the target list, so the two
    // indices coincide, and inserting before the hovered card is what you want anyway.
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

    try {
      await moveCard(boardId, cardId, targetListId, position);
    } catch (err) {
      // Put it back where it was. The server refused (an archived board, a lost membership),
      // and leaving the optimistic position on screen would be a lie.
      queryClient.setQueryData(sourceKey, sourceSnapshot);
      queryClient.setQueryData(targetKey, targetSnapshot);

      // A 409 is the optimistic-concurrency conflict: someone else moved this same card first and
      // won. Their target may be a third list, neither our source nor our target, which the finally
      // below (it refetches only those two) would never reach, leaving the card missing until
      // realtime caught up. Invalidate the whole board so we converge on the winner's column now,
      // with a message that says the card is not lost and there is nothing to retry.
      if (err instanceof ApiError && err.status === 409) {
        void queryClient.invalidateQueries({ queryKey: ["boards", boardId] });
        show("Someone else moved this card first — showing where it is now.");
      } else {
        show(err instanceof Error ? err.message : "Could not move the card.");
      }
    } finally {
      // Ranks are the server's to assign: refetch rather than trust our guess at the order.
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
          {renamingBoard ? (
            <form className="board-rename" onSubmit={submitBoardRename}>
              <input
                value={boardName}
                onChange={(e) => setBoardName(e.target.value)}
                onBlur={() => setRenamingBoard(false)}
                onKeyDown={(e) => e.key === "Escape" && setRenamingBoard(false)}
                maxLength={200}
                autoFocus
              />
            </form>
          ) : (
            <>
              <h1>{board.data?.name}</h1>
              {canRenameBoard && (
                <button
                  className="icon-btn"
                  aria-label={`Rename ${board.data?.name}`}
                  title="Rename board"
                  onClick={() => {
                    setBoardName(board.data!.name);
                    setRenamingBoard(true);
                  }}
                >
                  <Pencil size={15} />
                </button>
              )}
            </>
          )}
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
          <button
            className={panel === "settings" ? "icon-btn on" : "icon-btn"}
            aria-label="Board settings" title="Board settings"
            onClick={() => togglePanel("settings")}
          >
            <Settings size={17} />
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
          // Off because dnd-kit's built-in auto-scroll ramps against the viewport edge: hovering a
          // card in a right-hand column scrolls the whole board sideways, sliding the drop target
          // out from under the cursor so `over` goes null and the drop fails. The board never
          // scrolls vertically (`.board-body` is overflow:hidden), and reaching cards below a tall
          // column's fold is handled by `runAutoScroll` above.
          autoScroll={false}
          collisionDetection={collisionDetection}
          // Cards appear, vanish and reflow under the pointer; stale rects would send a drop to
          // the wrong slot.
          measuring={{ droppable: { strategy: MeasuringStrategy.Always } }}
          onDragStart={onDragStart}
          onDragEnd={(e) => void onDragEnd(e)}
          onDragCancel={onDragCancel}
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

          {/* Follows the cursor. Rendering the drag on a separate layer lets the card in the
              column stay put and act as a placeholder. */}
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
        {panel === "settings" && (
          <BoardSettingsPanel boardId={boardId} onClose={() => setPanel(null)} />
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
    </div>
  );
}
