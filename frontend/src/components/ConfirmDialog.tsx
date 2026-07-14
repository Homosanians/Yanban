import { useEffect, useRef } from "react";

interface Props {
  title: string;
  body?: string;
  confirmLabel?: string;
  danger?: boolean;
  pending?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * The one gate in front of everything irreversible. Nothing here is destructive by itself —
 * it renders a question and hands back an answer.
 */
export function ConfirmDialog({
  title,
  body,
  confirmLabel = "Confirm",
  danger = true,
  pending = false,
  onConfirm,
  onCancel,
}: Props) {
  const cancelRef = useRef<HTMLButtonElement>(null);

  // Focus lands on Cancel, not Confirm: a dialog that appears under a stray Enter keypress must
  // not then delete something because Enter was still on its way down.
  useEffect(() => cancelRef.current?.focus(), []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onCancel();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onCancel]);

  return (
    <>
      <div className="overlay above" onClick={onCancel} />
      <div className="confirm-wrap">
        <div className="confirm" role="alertdialog" aria-modal="true" aria-label={title}>
          <h2>{title}</h2>
          {body && <p className="muted">{body}</p>}
          <div className="actions">
            <button ref={cancelRef} type="button" className="secondary" onClick={onCancel}>
              Cancel
            </button>
            <button
              type="button"
              className={danger ? "danger" : undefined}
              disabled={pending}
              onClick={onConfirm}
            >
              {pending ? "Working..." : confirmLabel}
            </button>
          </div>
        </div>
      </div>
    </>
  );
}
