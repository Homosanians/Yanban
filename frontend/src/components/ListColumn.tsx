import { useState } from "react";
import type { FormEvent, KeyboardEvent } from "react";
import { useDroppable } from "@dnd-kit/core";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { contentKeys, createCard, deleteList, renameList } from "../api/board-content";
import type { BoardList, BoardMember, Card } from "../types";
import { CardTile } from "./CardTile";
import { ConfirmDialog } from "./ConfirmDialog";

interface Props {
  boardId: string;
  list: BoardList;
  cards: Card[];
  members: BoardMember[];
  writable: boolean;
  onOpenCard: (cardId: string) => void;
  onHoverCard: (cardId: string | null) => void;
}

export function ListColumn({
  boardId, list, cards, members, writable, onOpenCard, onHoverCard,
}: Props) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState("");
  const [adding, setAdding] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const [name, setName] = useState(list.name);
  const [confirmDelete, setConfirmDelete] = useState(false);

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
    onSuccess: () => {
      setConfirmDelete(false);
      void queryClient.invalidateQueries({ queryKey: contentKeys.lists(boardId) });
    },
  });

  const submitCard = () => {
    if (!title.trim()) return;
    add.mutate(title.trim());
    // Stay open: adding one card is almost always adding three.
  };

  const onCardKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submitCard();
    } else if (e.key === "Escape") {
      setAdding(false);
      setTitle("");
    }
  };

  const onRename = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim()) rename.mutate(name.trim());
  };

  return (
    <section className={isOver ? "column over" : "column"} ref={setNodeRef}>
      <header className="column-head">
        {renaming ? (
          <form onSubmit={onRename}>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              onBlur={() => setRenaming(false)}
              onKeyDown={(e) => e.key === "Escape" && setRenaming(false)}
              maxLength={200}
              autoFocus
            />
          </form>
        ) : (
          <>
            <h2>{list.name}</h2>
            <span className="count">{cards.length}</span>
            {writable && (
              <span className="actions">
                <button
                  className="icon-btn"
                  aria-label={`Rename ${list.name}`}
                  title="Rename list"
                  onClick={() => { setName(list.name); setRenaming(true); }}
                >
                  <Pencil size={14} />
                </button>
                <button
                  className="icon-btn danger"
                  aria-label={`Delete ${list.name}`}
                  title="Delete list"
                  onClick={() => setConfirmDelete(true)}
                >
                  <Trash2 size={14} />
                </button>
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
              onOpen={() => onOpenCard(card.id)}
              onHover={onHoverCard}
            />
          ))}
        </div>
      </SortableContext>

      {writable && (adding ? (
        <div className="add-form">
          <textarea
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            onKeyDown={onCardKeyDown}
            placeholder="What needs doing?"
            rows={2}
            maxLength={500}
            autoFocus
          />
          <div className="row">
            <button type="button" onClick={submitCard} disabled={!title.trim() || add.isPending}>
              Add card
            </button>
            <button
              type="button"
              className="ghost"
              onClick={() => { setAdding(false); setTitle(""); }}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <button className="add-trigger" onClick={() => setAdding(true)}>
          <Plus size={15} />
          Add a card
        </button>
      ))}

      {confirmDelete && (
        <ConfirmDialog
          title={`Delete “${list.name}”?`}
          body="The list and every card in it will be deleted. This cannot be undone."
          confirmLabel="Delete list"
          pending={remove.isPending}
          onConfirm={() => remove.mutate()}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </section>
  );
}
