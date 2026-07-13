import { useState } from "react";
import type { FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  contentKeys,
  createCardFromTemplate,
  createTemplate,
  deleteTemplate,
  listLists,
  listTemplates,
} from "../api/board-content";

interface Props {
  boardId: string;
  writable: boolean;
  onClose: () => void;
}

export function TemplatesPanel({ boardId, writable, onClose }: Props) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  // Empty until the user picks one; `listId` below resolves that to the shown default. Anything
  // that acts on the selection must use `listId`, not this — they differ until the first change.
  const [targetList, setTargetList] = useState("");

  const templates = useQuery({
    queryKey: contentKeys.templates(boardId),
    queryFn: () => listTemplates(boardId),
  });
  const lists = useQuery({ queryKey: contentKeys.lists(boardId), queryFn: () => listLists(boardId) });

  // The list the dropdown is actually showing: the user's pick, or the first list as the default.
  const listId = targetList || lists.data?.[0]?.id || "";

  const add = useMutation({
    mutationFn: () =>
      createTemplate(boardId, {
        name: name.trim(),
        title: title.trim(),
        description: description.trim() ? description : null,
      }),
    onSuccess: () => {
      setName("");
      setTitle("");
      setDescription("");
      void queryClient.invalidateQueries({ queryKey: contentKeys.templates(boardId) });
    },
  });

  const remove = useMutation({
    mutationFn: (templateId: string) => deleteTemplate(boardId, templateId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: contentKeys.templates(boardId) }),
  });

  // A template is a blueprint stamped onto a new card, not a live link: editing it later never
  // rewrites cards already made from it (ADR-12).
  const apply = useMutation({
    mutationFn: (templateId: string) => createCardFromTemplate(boardId, listId, templateId),
    onSuccess: (card) =>
      void queryClient.invalidateQueries({ queryKey: contentKeys.cards(boardId, card.listId) }),
  });

  const onAdd = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim() && title.trim()) add.mutate();
  };

  return (
    <aside className="panel">
      <header className="drawer-head">
        <h2>Templates</h2>
        <button className="ghost" onClick={onClose}>Close</button>
      </header>

      {writable && (
        <form className="stack" onSubmit={onAdd}>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Template name" maxLength={200} />
          <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Card title" maxLength={500} />
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Card description"
            rows={3}
            maxLength={10000}
          />
          <button type="submit" disabled={add.isPending || !name.trim() || !title.trim()}>Save template</button>
          {add.isError && <p className="error">{(add.error as Error).message}</p>}
        </form>
      )}

      {writable && lists.data && lists.data.length > 0 && (
        <label className="stack">
          Add cards to
          <select value={listId} onChange={(e) => setTargetList(e.target.value)}>
            {lists.data.map((l) => (
              <option key={l.id} value={l.id}>{l.name}</option>
            ))}
          </select>
        </label>
      )}

      <ul className="plain">
        {templates.data?.map((t) => (
          <li key={t.id} className="member">
            <div>
              <strong>{t.name}</strong>
              <div className="muted">{t.title}</div>
            </div>
            <div className="row">
              {writable && listId && (
                <button onClick={() => apply.mutate(t.id)} disabled={apply.isPending}>Use</button>
              )}
              {writable && <button className="ghost danger" onClick={() => remove.mutate(t.id)}>Delete</button>}
            </div>
          </li>
        ))}
      </ul>
      {templates.data?.length === 0 && <p className="muted">No templates yet.</p>}
      {apply.isError && <p className="error">{(apply.error as Error).message}</p>}
    </aside>
  );
}
