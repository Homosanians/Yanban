import { useState } from "react";
import type { FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { DndContext, PointerSensor, closestCorners, useSensor, useSensors } from "@dnd-kit/core";
import type { DragEndEvent, DragStartEvent } from "@dnd-kit/core";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { boardKeys, getBoard, listMembers } from "../api/boards";
import { contentKeys, createList, listCards, listLists, moveCard } from "../api/board-content";
import { useAuth } from "../auth/useAuth";
import { useBoardRealtime } from "../realtime/useBoardRealtime";
import { canWrite } from "../types";
import type { Card } from "../types";
import { ListColumn } from "../components/ListColumn";
import { CardDetail } from "../components/CardDetail";
import { MembersPanel } from "../components/MembersPanel";
import { ActivityFeed } from "../components/ActivityFeed";
import { SearchBox } from "../components/SearchBox";
import { TemplatesPanel } from "../components/TemplatesPanel";

type SidePanel = "members" | "activity" | "templates" | null;

export function BoardPage() {
  const { boardId = "" } = useParams();
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();

  const [openCardId, setOpenCardId] = useState<string | null>(null);
  const [panel, setPanel] = useState<SidePanel>(null);
  const [newListName, setNewListName] = useState("");
  const [dragError, setDragError] = useState<string | null>(null);

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
      void queryClient.invalidateQueries({ queryKey: contentKeys.lists(boardId) });
    },
  });

  // A click must not be swallowed as a drag: without a distance threshold, opening a card
  // would start a 0-pixel drag instead.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));

  const [dragging, setDragging] = useState<string | null>(null);
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

    // The API's `position` is an index among the target list's *other* cards, so the dragged
    // card is taken out before the index is read. That is also what makes a same-list move
    // downwards land where the pointer is rather than one slot short.
    const withoutDragged = cardsByList[targetListId].filter((c) => c.id !== cardId);
    const overIndex = overIsList ? withoutDragged.length : withoutDragged.findIndex((c) => c.id === overId);
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

  const onAddList = (e: FormEvent) => {
    e.preventDefault();
    if (newListName.trim()) addList.mutate(newListName.trim());
  };

  if (board.isLoading) return <p className="pad">Loading board...</p>;
  if (board.isError) {
    // A non-member gets a real message rather than a blank board.
    return (
      <div className="pad">
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
          <Link to="/" className="ghost">&larr; Boards</Link>
          <h1>{board.data?.name}</h1>
          {board.data?.archived && <span className="badge">archived (read-only)</span>}
        </div>
        <SearchBox boardId={boardId} onOpenCard={setOpenCardId} />
        <div className="user">
          <button className="ghost" onClick={() => setPanel(panel === "members" ? null : "members")}>Members</button>
          <button className="ghost" onClick={() => setPanel(panel === "templates" ? null : "templates")}>Templates</button>
          <button className="ghost" onClick={() => setPanel(panel === "activity" ? null : "activity")}>Activity</button>
          <span>{user?.displayName}</span>
          <button className="ghost" onClick={() => void logout()}>Log out</button>
        </div>
      </header>

      {dragError && <p className="error pad">{dragError}</p>}

      <div className="board-body">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCorners}
          onDragStart={onDragStart}
          onDragEnd={(e) => void onDragEnd(e)}
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
                draggingId={dragging}
                onOpenCard={setOpenCardId}
              />
            ))}

            {writable && (
              <form className="column add-column" onSubmit={onAddList}>
                <input
                  value={newListName}
                  onChange={(e) => setNewListName(e.target.value)}
                  placeholder="Add a list"
                  maxLength={200}
                />
                <button type="submit" disabled={!newListName.trim()}>Add list</button>
              </form>
            )}
          </div>
        </DndContext>

        {panel === "members" && <MembersPanel boardId={boardId} role={board.data!.role} onClose={() => setPanel(null)} />}
        {panel === "activity" && <ActivityFeed boardId={boardId} onClose={() => setPanel(null)} />}
        {panel === "templates" && (
          <TemplatesPanel boardId={boardId} writable={writable} onClose={() => setPanel(null)} />
        )}
      </div>

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

      {activeCard && <div className="drag-hint">Moving “{activeCard.title}”</div>}
    </div>
  );
}
