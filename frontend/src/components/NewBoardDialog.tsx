import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";

interface Props {
  pending: boolean;
  error?: string | null;
  onCreate: (name: string, seedDefaultLists: boolean) => void;
  onCancel: () => void;
}

/**
 * Creating a board is a decision with two parts — a name and whether to start from the template —
 * so it gets a dialog rather than an input wedged into a 230px tile. Shares the shell with
 * ConfirmDialog: one centred-dialog look in the app, not two.
 */
export function NewBoardDialog({ pending, error, onCreate, onCancel }: Props) {
  const [name, setName] = useState("");
  const [seed, setSeed] = useState(true);
  const nameRef = useRef<HTMLInputElement>(null);

  // Unlike the confirm dialog — which focuses Cancel, because it guards a destruction — this one
  // exists to be filled in, so focus goes where the typing starts.
  useEffect(() => nameRef.current?.focus(), []);

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

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim()) onCreate(name.trim(), seed);
  };

  return (
    <>
      <div className="overlay above" onClick={onCancel} />
      <div className="confirm-wrap">
        <div className="confirm" role="dialog" aria-modal="true" aria-label="New board">
          <h2>New board</h2>
          <form onSubmit={onSubmit}>
            <label>
              Name
              <input
                ref={nameRef}
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Q3 roadmap"
                maxLength={200}
              />
            </label>

            <label className="check">
              <input type="checkbox" checked={seed} onChange={(e) => setSeed(e.target.checked)} />
              Start with Backlog · To Do · Doing · Done
            </label>

            {error && <p className="error">{error}</p>}

            <div className="actions">
              <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
              <button type="submit" disabled={!name.trim() || pending}>
                {pending ? "Creating…" : "Create board"}
              </button>
            </div>
          </form>
        </div>
      </div>
    </>
  );
}
