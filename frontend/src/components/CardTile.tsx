import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { BoardMember, Card } from "../types";

interface Props {
  card: Card;
  members: BoardMember[];
  draggable: boolean;
  dragging: boolean;
  onOpen: () => void;
}

export function CardTile({ card, members, draggable, dragging, onOpen }: Props) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({
    id: card.id,
    disabled: !draggable,
  });

  const assignee = members.find((m) => m.userId === card.assigneeId);
  const due = card.dueDate ? new Date(card.dueDate) : null;
  const overdue = due !== null && due.getTime() < Date.now();

  return (
    <article
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={dragging ? "card is-dragging" : "card"}
      onClick={onOpen}
      {...attributes}
      {...listeners}
    >
      <p className="card-title">{card.title}</p>
      <div className="card-meta">
        {due && (
          <span className={overdue ? "due overdue" : "due"}>{due.toLocaleDateString()}</span>
        )}
        {assignee && <span className="avatar" title={assignee.email}>{initials(assignee.displayName)}</span>}
      </div>
    </article>
  );
}

const initials = (name: string): string =>
  name
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join("");
