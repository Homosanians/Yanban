import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { AlertTriangle, CalendarDays } from "lucide-react";
import type { BoardMember, Card } from "../types";
import { formatDue, isOverdue } from "../lib/due";
import { Avatar } from "./Avatar";

interface FaceProps {
  card: Card;
  members: BoardMember[];
}

/** The card's contents, with no drag machinery — shared by the real tile and the drag overlay. */
function CardFace({ card, members }: FaceProps) {
  const assignee = members.find((m) => m.userId === card.assigneeId);
  const overdue = isOverdue(card.dueDate);

  return (
    <>
      {overdue && (
        <div className="card-flags">
          <span className="flag">
            <AlertTriangle size={10} />
            Overdue
          </span>
        </div>
      )}
      <p className="card-title">{card.title}</p>
      <div className="card-meta">
        {card.dueDate && (
          <span className={overdue ? "chip overdue" : "chip"}>
            <CalendarDays size={11} />
            {formatDue(card.dueDate)}
          </span>
        )}
        {assignee && <Avatar email={assignee.email} name={assignee.displayName} size="sm" />}
      </div>
    </>
  );
}

/**
 * The copy that follows the cursor.
 *
 * A separate component on purpose: it must *not* call useSortable. The overlay lives inside the
 * DndContext, so a sortable hook here would register a second droppable under the id of the card
 * being dragged — the drag would be measuring itself.
 */
export function CardOverlay({ card, members }: FaceProps) {
  return (
    <article className="card overlay">
      <CardFace card={card} members={members} />
    </article>
  );
}

interface Props extends FaceProps {
  draggable: boolean;
  onOpen: () => void;
  /** Tells the board which card the Space shortcut would act on. */
  onHover: (cardId: string | null) => void;
}

export function CardTile({ card, members, draggable, onOpen, onHover }: Props) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: card.id,
    disabled: !draggable,
  });

  return (
    <article
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      // While it is being dragged this element is just a hole in the layout: the DragOverlay is
      // what the cursor carries. It still slides with the sort strategy, which is how you see
      // where the card is about to land.
      className={isDragging ? "card placeholder" : "card"}
      onClick={onOpen}
      onPointerEnter={() => onHover(card.id)}
      onPointerLeave={() => onHover(null)}
      {...attributes}
      {...listeners}
    >
      <CardFace card={card} members={members} />
    </article>
  );
}
