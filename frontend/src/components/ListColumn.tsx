import { useState } from "react";
import type { FormEvent } from "react";
import { useDroppable } from "@dnd-kit/core";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { contentKeys, createCard, deleteList, renameList } from "../api/board-content";
import type { BoardList, BoardMember, Card } from "../types";
import { CardTile } from "./CardTile";

interface Props {
  boardId: string;
  list: BoardList;
  cards: Card[];
  members: BoardMember[];
  writable: boolean;
  draggingId: string | null;
  onOpenCard: (cardId: string) => void;
}

export function ListColumn({ boardId, list, cards, members, writable, draggingId, onOpenCard }: Props) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState("");
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(list.name);

  // The column itself is a drop target, so a card can be dropped into a list that has no
  // cards to aim at.
  const { setNodeRef, isOver } = useDroppable({ id: list.id });

  const add = useMutation({
    mutationFn: (cardTitle: string) => createCard(boardId, list.id, cardTitle),
    onSuccess: () => {
      setTitle("");
      void queryClient.invalidateQueries({ queryKey: contentKeys.cards(boardId, list.id) });
    },
  });

  const rename = useMutation({
    mutationFn: (newName: string) => renameList(boardId, list.id, newName),
    onSuccess: () => {
      setRenaming(false);
      void queryClient.invalidateQueries({ queryKey: contentKeys.lists(boardId) });
    },
  });

  const remove = useMutation({
    mutationFn: () => deleteList(boardId, list.id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: contentKeys.lists(boardId) }),
  });

  const onAdd = (e: FormEvent) => {
    e.preventDefault();
    if (title.trim()) add.mutate(title.trim());
  };

  const onRename = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim()) rename.mutate(name.trim());
  };

  return (
    <section className={isOver ? "column over" : "column"} ref={setNodeRef}>
      <header className="column-head">
        {renaming ? (
          <form onSubmit={onRename} className="row">
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={200} autoFocus />
            <button type="submit">Save</button>
            <button type="button" className="ghost" onClick={() => setRenaming(false)}>Cancel</button>
          </form>
        ) : (
          <>
            <h2>{list.name}</h2>
            <span className="count">{cards.length}</span>
            {writable && (
              <span className="actions">
                <button className="ghost" onClick={() => setRenaming(true)}>Rename</button>
                <button className="ghost danger" onClick={() => remove.mutate()}>Delete</button>
              </span>
            )}
          </>
        )}
      </header>

      <SortableContext items={cards.map((c) => c.id)} strategy={verticalListSortingStrategy}>
        <div className="cards">
          {cards.map((card) => (
            <CardTile
              key={card.id}
              card={card}
              members={members}
              draggable={writable}
              dragging={draggingId === card.id}
              onOpen={() => onOpenCard(card.id)}
            />
          ))}
        </div>
      </SortableContext>

      {writable && (
        <form className="add-card" onSubmit={onAdd}>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Add a card"
            maxLength={500}
          />
          <button type="submit" disabled={!title.trim()}>Add</button>
        </form>
      )}
    </section>
  );
}
