import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  assignCard,
  contentKeys,
  createComment,
  deleteAttachment,
  deleteCard,
  deleteComment,
  getCard,
  getDownloadUrl,
  listAttachments,
  listComments,
  updateCard,
  uploadAttachment,
} from "../api/board-content";
import { ApiError } from "../lib/apiClient";
import type { BoardMember } from "../types";

interface Props {
  boardId: string;
  cardId: string;
  members: BoardMember[];
  writable: boolean;
  selfId: string;
  onClose: () => void;
}

export function CardDetail({ boardId, cardId, members, writable, selfId, onClose }: Props) {
  const queryClient = useQueryClient();

  const card = useQuery({
    queryKey: contentKeys.card(boardId, cardId),
    queryFn: () => getCard(boardId, cardId),
  });
  const comments = useQuery({
    queryKey: contentKeys.comments(boardId, cardId),
    queryFn: () => listComments(boardId, cardId),
  });
  const attachments = useQuery({
    queryKey: contentKeys.attachments(boardId, cardId),
    queryFn: () => listAttachments(boardId, cardId),
  });

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [conflict, setConflict] = useState<string | null>(null);
  const [body, setBody] = useState("");

  // Load the server's copy into the form. Runs again after a conflict refetch, which is how
  // the user gets to see what the other person actually wrote.
  useEffect(() => {
    if (!card.data) return;
    setTitle(card.data.title);
    setDescription(card.data.description ?? "");
    setDueDate(card.data.dueDate ? card.data.dueDate.slice(0, 10) : "");
  }, [card.data]);

  const invalidateBoard = () => {
    void queryClient.invalidateQueries({ queryKey: contentKeys.card(boardId, cardId) });
    void queryClient.invalidateQueries({ queryKey: ["boards", boardId, "lists"] });
    (queryClient.getQueryCache().findAll({ queryKey: ["boards", boardId] }) ?? []).forEach((q) =>
      void queryClient.invalidateQueries({ queryKey: q.queryKey }),
    );
  };

  const save = useMutation({
    mutationFn: () =>
      updateCard(boardId, cardId, card.data!.version, {
        title: title.trim(),
        description: description.trim() ? description : null,
        // The API takes a DateTimeOffset; a bare date input has no time, so pin it to UTC midnight.
        dueDate: dueDate ? new Date(`${dueDate}T00:00:00Z`).toISOString() : null,
      }),
    onSuccess: () => {
      setConflict(null);
      invalidateBoard();
    },
    onError: (err) => {
      // 412: the card moved under us. Never retry without If-Match — silently overwriting
      // someone else's edit is exactly what the version check exists to prevent. Refetch and
      // make the user look at the current text before they decide.
      if (err instanceof ApiError && err.status === 412) {
        setConflict("Someone else changed this card while you were editing. Your changes were not saved — the current version is shown below.");
        void card.refetch();
      }
    },
  });

  const assign = useMutation({
    mutationFn: (assigneeId: string | null) => assignCard(boardId, cardId, assigneeId),
    onSuccess: invalidateBoard,
  });

  const remove = useMutation({
    mutationFn: () => deleteCard(boardId, cardId),
    onSuccess: () => {
      invalidateBoard();
      onClose();
    },
  });

  const addComment = useMutation({
    mutationFn: (text: string) => createComment(boardId, cardId, text),
    onSuccess: () => {
      setBody("");
      void queryClient.invalidateQueries({ queryKey: contentKeys.comments(boardId, cardId) });
    },
  });

  const removeComment = useMutation({
    mutationFn: (commentId: string) => deleteComment(boardId, cardId, commentId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: contentKeys.comments(boardId, cardId) }),
  });

  const upload = useMutation({
    mutationFn: (file: File) => uploadAttachment(boardId, cardId, file),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: contentKeys.attachments(boardId, cardId) }),
  });

  const removeAttachment = useMutation({
    mutationFn: (attachmentId: string) => deleteAttachment(boardId, cardId, attachmentId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: contentKeys.attachments(boardId, cardId) }),
  });

  const download = async (attachmentId: string) => {
    // The API mints a short-lived presigned URL and the browser fetches the bytes straight
    // from storage — they never pass through the API (ADR-10).
    const { downloadUrl } = await getDownloadUrl(boardId, cardId, attachmentId);
    window.open(downloadUrl, "_blank", "noopener");
  };

  const onSave = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <div className="drawer-backdrop" onClick={onClose}>
      <aside className="drawer" onClick={(e) => e.stopPropagation()}>
        <header className="drawer-head">
          <h2>Card</h2>
          <button className="ghost" onClick={onClose}>Close</button>
        </header>

        {card.isLoading && <p>Loading...</p>}
        {card.isError && <p className="error">{(card.error as Error).message}</p>}

        {card.data && (
          <>
            {conflict && <p className="conflict">{conflict}</p>}

            <form onSubmit={onSave} className="stack">
              <label>
                Title
                <input value={title} onChange={(e) => setTitle(e.target.value)} disabled={!writable} maxLength={500} />
              </label>
              <label>
                Description
                <textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  disabled={!writable}
                  rows={5}
                  maxLength={10000}
                />
              </label>
              <label>
                Due date
                <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} disabled={!writable} />
              </label>
              {writable && (
                <div className="row">
                  <button type="submit" disabled={save.isPending || !title.trim()}>Save</button>
                  <button type="button" className="ghost danger" onClick={() => remove.mutate()}>Delete card</button>
                </div>
              )}
              {save.isError && !conflict && <p className="error">{(save.error as Error).message}</p>}
            </form>

            <label className="stack">
              Assignee
              <select
                value={card.data.assigneeId ?? ""}
                disabled={!writable}
                onChange={(e) => assign.mutate(e.target.value || null)}
              >
                <option value="">Unassigned</option>
                {members.map((m) => (
                  <option key={m.userId} value={m.userId}>{m.displayName}</option>
                ))}
              </select>
            </label>

            <section className="stack">
              <h3>Attachments</h3>
              {writable && (
                <input
                  type="file"
                  onChange={(e) => {
                    const file = e.target.files?.[0];
                    if (file) upload.mutate(file);
                    e.target.value = "";
                  }}
                />
              )}
              {upload.isPending && <p className="muted">Uploading...</p>}
              {upload.isError && <p className="error">{(upload.error as Error).message}</p>}
              <ul className="plain">
                {attachments.data?.map((a) => (
                  <li key={a.id} className="row between">
                    <button className="link" onClick={() => void download(a.id)}>{a.fileName}</button>
                    <span className="muted">{Math.ceil(a.sizeBytes / 1024)} KB</span>
                    {writable && (
                      <button className="ghost danger" onClick={() => removeAttachment.mutate(a.id)}>Remove</button>
                    )}
                  </li>
                ))}
              </ul>
              {attachments.data?.length === 0 && <p className="muted">No attachments.</p>}
            </section>

            <section className="stack">
              <h3>Comments</h3>
              <ul className="plain">
                {comments.data?.map((c) => (
                  <li key={c.id} className="comment">
                    <div className="row between">
                      <strong>{c.authorDisplayName}</strong>
                      <span className="muted">{new Date(c.createdAt).toLocaleString()}</span>
                    </div>
                    <p>{c.body}</p>
                    {/* The API also lets a board admin delete anyone's comment; this only offers
                        the case every member always has. */}
                    {c.authorId === selfId && (
                      <button className="ghost danger" onClick={() => removeComment.mutate(c.id)}>Delete</button>
                    )}
                  </li>
                ))}
              </ul>
              {writable && (
                <form
                  className="row"
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (body.trim()) addComment.mutate(body.trim());
                  }}
                >
                  <input
                    value={body}
                    onChange={(e) => setBody(e.target.value)}
                    placeholder="Write a comment"
                    maxLength={5000}
                  />
                  <button type="submit" disabled={!body.trim()}>Comment</button>
                </form>
              )}
            </section>
          </>
        )}
      </aside>
    </div>
  );
}
