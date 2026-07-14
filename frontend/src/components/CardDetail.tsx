import { useEffect, useLayoutEffect, useState } from "react";
import type { FormEvent } from "react";
import { AlertTriangle, Download, Paperclip, Trash2, X } from "lucide-react";
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
import { isOverdue } from "../lib/due";
import type { BoardMember } from "../types";
import { Avatar } from "./Avatar";
import { ConfirmDialog } from "./ConfirmDialog";

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
  const [confirmDelete, setConfirmDelete] = useState(false);

  // Load the server's copy into the form. Runs again after a conflict refetch, which is how
  // the user gets to see what the other person actually wrote.
  //
  // Layout, not passive: a plain useEffect runs *after* paint, so the drawer rendered one frame
  // with an empty title box before filling it in.
  useLayoutEffect(() => {
    if (!card.data) return;
    setTitle(card.data.title);
    setDescription(card.data.description ?? "");
    setDueDate(card.data.dueDate ? card.data.dueDate.slice(0, 10) : "");
  }, [card.data]);

  // Escape closes the drawer — but not while a confirm dialog is up, which owns Escape itself.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !confirmDelete) onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, confirmDelete]);

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

  const assignee = members.find((m) => m.userId === card.data?.assigneeId);

  return (
    <div className="drawer-backdrop" onClick={onClose}>
      <aside className="drawer" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Card">
        <header className="drawer-head">
          <h2>Card</h2>
          <div className="row">
            {writable && (
              <button
                className="icon-btn danger"
                aria-label="Delete card"
                title="Delete card"
                onClick={() => setConfirmDelete(true)}
              >
                <Trash2 size={16} />
              </button>
            )}
            <button className="icon-btn" aria-label="Close card" title="Close" onClick={onClose}>
              <X size={16} />
            </button>
          </div>
        </header>

        <div className="drawer-body">
          {card.isLoading && <p className="muted">Loading…</p>}
          {card.isError && <p className="error">{(card.error as Error).message}</p>}

          {card.data && (
            <>
              {conflict && <p className="conflict">{conflict}</p>}

              <form onSubmit={onSave} className="stack">
                <input
                  className="card-title-input"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  disabled={!writable}
                  maxLength={500}
                  aria-label="Card title"
                />
                <label>
                  Description
                  <textarea
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    disabled={!writable}
                    rows={5}
                    maxLength={10000}
                    placeholder="Add more detail…"
                  />
                </label>
                <div className="row">
                  <label className="grow">
                    <span className="label-row">
                      Due date
                      {/* Read from the saved card, not from the input beside it: the flag reports
                          what the board is showing everyone else, not a date you are still typing. */}
                      {isOverdue(card.data.dueDate) && (
                        <span className="flag">
                          <AlertTriangle size={10} />
                          Overdue
                        </span>
                      )}
                    </span>
                    <input
                      type="date"
                      value={dueDate}
                      onChange={(e) => setDueDate(e.target.value)}
                      disabled={!writable}
                    />
                  </label>
                  <label className="grow">
                    Assignee
                    <div className="row">
                      {assignee && <Avatar email={assignee.email} name={assignee.displayName} />}
                      <select
                        className="grow"
                        value={card.data.assigneeId ?? ""}
                        disabled={!writable}
                        onChange={(e) => assign.mutate(e.target.value || null)}
                      >
                        <option value="">Unassigned</option>
                        {members.map((m) => (
                          <option key={m.userId} value={m.userId}>{m.displayName}</option>
                        ))}
                      </select>
                    </div>
                  </label>
                </div>
                {writable && (
                  <div className="row">
                    <button type="submit" disabled={save.isPending || !title.trim()}>Save changes</button>
                  </div>
                )}
                {save.isError && !conflict && <p className="error">{(save.error as Error).message}</p>}
              </form>

              <section>
                <h3>Attachments</h3>
                {writable && (
                  <label className="file-drop">
                    <Paperclip size={15} />
                    {upload.isPending ? "Uploading…" : "Attach a file"}
                    <input
                      type="file"
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file) upload.mutate(file);
                        e.target.value = "";
                      }}
                    />
                  </label>
                )}
                {upload.isError && <p className="error">{(upload.error as Error).message}</p>}

                {attachments.data?.map((a) => (
                  <div key={a.id} className="attachment">
                    <Paperclip size={14} />
                    <button className="link grow truncate" onClick={() => void download(a.id)}>
                      {a.fileName}
                    </button>
                    <span className="faint">{Math.ceil(a.sizeBytes / 1024)} KB</span>
                    <button
                      className="icon-btn"
                      aria-label={`Download ${a.fileName}`}
                      title="Download"
                      onClick={() => void download(a.id)}
                    >
                      <Download size={14} />
                    </button>
                    {writable && (
                      <button
                        className="icon-btn danger"
                        aria-label={`Remove ${a.fileName}`}
                        title="Remove"
                        onClick={() => removeAttachment.mutate(a.id)}
                      >
                        <Trash2 size={14} />
                      </button>
                    )}
                  </div>
                ))}
                {attachments.data?.length === 0 && <p className="faint">No attachments.</p>}
              </section>

              <section>
                <h3>Comments</h3>
                {comments.data?.map((c) => {
                  const author = members.find((m) => m.userId === c.authorId);
                  return (
                    <div key={c.id} className="comment">
                      {author && <Avatar email={author.email} name={author.displayName} size="sm" />}
                      <div className="body">
                        <div className="row between">
                          <strong>{c.authorDisplayName}</strong>
                          <span className="faint">{new Date(c.createdAt).toLocaleString()}</span>
                        </div>
                        <p>{c.body}</p>
                      </div>
                      {/* The API also lets a board admin delete anyone's comment; this only offers
                          the case every member always has. */}
                      {c.authorId === selfId && (
                        <button
                          className="icon-btn danger"
                          aria-label="Delete comment"
                          title="Delete comment"
                          onClick={() => removeComment.mutate(c.id)}
                        >
                          <Trash2 size={14} />
                        </button>
                      )}
                    </div>
                  );
                })}
                {comments.data?.length === 0 && <p className="faint">No comments yet.</p>}

                {writable && (
                  <form
                    className="row"
                    onSubmit={(e) => {
                      e.preventDefault();
                      if (body.trim()) addComment.mutate(body.trim());
                    }}
                  >
                    <input
                      className="grow"
                      value={body}
                      onChange={(e) => setBody(e.target.value)}
                      placeholder="Write a comment"
                      maxLength={5000}
                    />
                    <button type="submit" disabled={!body.trim() || addComment.isPending}>Send</button>
                  </form>
                )}
              </section>
            </>
          )}
        </div>

        {confirmDelete && (
          <ConfirmDialog
            title="Delete this card?"
            body="The card, its comments and its attachments will be deleted. This cannot be undone."
            confirmLabel="Delete card"
            pending={remove.isPending}
            onConfirm={() => remove.mutate()}
            onCancel={() => setConfirmDelete(false)}
          />
        )}
      </aside>
    </div>
  );
}
