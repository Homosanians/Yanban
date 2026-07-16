import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { listBoardTemplates } from "../api/boards";

interface Props {
  pending: boolean;
  error?: string | null;
  onCreate: (name: string, template: string | null) => void;
  onCancel: () => void;
}

/**
 * Creating a board has two parts, a name and which layout to start from, so it gets a dialog
 * rather than an input wedged into a 230px tile. Shares the shell with ConfirmDialog: one
 * centred-dialog look in the app, not two.
 *
 * The templates come from the server (they are configurable there), so this is a fetched list of
 * radio options, with "Empty board" always first and pre-selected. Starting blank is a real,
 * common choice, not a fallback.
 */
export function NewBoardDialog({ pending, error, onCreate, onCancel }: Props) {
  const [name, setName] = useState("");
  // "" is the sentinel for an empty board; a real template id otherwise.
  const [template, setTemplate] = useState("");
  const nameRef = useRef<HTMLInputElement>(null);

  const templates = useQuery({ queryKey: ["board-templates"], queryFn: listBoardTemplates });

  // The confirm dialog focuses Cancel because it guards a destruction; this one exists to be
  // filled in, so focus goes where the typing starts.
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
    if (name.trim()) onCreate(name.trim(), template || null);
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

            <fieldset className="template-choice">
              <legend>Start from</legend>

              <label className="template-option">
                <input
                  type="radio"
                  name="template"
                  checked={template === ""}
                  onChange={() => setTemplate("")}
                />
                <span>
                  <strong>Empty board</strong>
                  <span className="faint">No lists — add your own</span>
                </span>
              </label>

              {templates.data?.map((t) => (
                <label key={t.id} className="template-option">
                  <input
                    type="radio"
                    name="template"
                    checked={template === t.id}
                    onChange={() => setTemplate(t.id)}
                  />
                  <span>
                    <strong>{t.name}</strong>
                    <span className="faint">{t.lists.join(" · ")}</span>
                  </span>
                </label>
              ))}
            </fieldset>

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
