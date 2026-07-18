import { useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ClipboardEvent } from "react";
import { AlertTriangle, Check, Download, Loader2, Paperclip, Save, SendHorizontal, Trash2, X } from "lucide-react";
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
import { boardSettingsKeys, getBoardUsage } from "../api/boards";
import { ApiError } from "../lib/apiClient";
import { formatBytes } from "../lib/bytes";
import { isOverdue } from "../lib/due";
import { useToast } from "../toast/useToast";
import type { BoardMember } from "../types";
import { AttachmentThumb } from "./AttachmentThumb";
import { Avatar } from "./Avatar";
import { ConfirmDialog } from "./ConfirmDialog";
import { DatePicker } from "./DatePicker";
import { Dropdown } from "./Dropdown";

interface Props {
  boardId: string;
  cardId: string;
  members: BoardMember[];
  writable: boolean;
  selfId: string;
  onClose: () => void;
}

type CardFields = { title: string; description: string; dueDate: string };

export function CardDetail({ boardId, cardId, members, writable, selfId, onClose }: Props) {
  const queryClient = useQueryClient();
  const { show } = useToast();

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

  // Autosave bookkeeping.
  // - savedRef: the values last persisted, to tell dirty from clean.
  // - versionRef: the If-Match token. Seeded from the card, then advanced from each write's
  //   response so back-to-back autosaves do not conflict with each other.
  // - latestRef: the current field values, so callbacks and timers read fresh values, not a
  //   closure snapshot.
  // - inFlight / resaveWanted: run one save at a time and, if edits arrived mid-flight, save once
  //   more when it finishes.
  const savedRef = useRef<CardFields | null>(null);
  const versionRef = useRef(0);
  const latestRef = useRef<CardFields>({ title: "", description: "", dueDate: "" });
  const seededFor = useRef<string | null>(null);
  const forceReseed = useRef(false);
  const inFlight = useRef(false);
  const resaveWanted = useRef(false);

  latestRef.current = { title: title.trim(), description, dueDate };

  const dirty =
    !!savedRef.current &&
    (latestRef.current.title !== savedRef.current.title ||
      latestRef.current.description !== savedRef.current.description ||
      latestRef.current.dueDate !== savedRef.current.dueDate);

  // Seed the form from the server on open, and again only when forced (after a conflict, so the
  // user is shown the current version). We do not reseed on every refetch, which would clobber
  // edits in progress.
  //
  // useLayoutEffect, not useEffect: a plain effect runs after paint, so the drawer would flash one
  // empty frame before filling in.
  useLayoutEffect(() => {
    if (!card.data) return;
    if (seededFor.current === cardId && !forceReseed.current) return;
    const t = card.data.title;
    const d = card.data.description ?? "";
    const due = card.data.dueDate ? card.data.dueDate.slice(0, 10) : "";
    setTitle(t);
    setDescription(d);
    setDueDate(due);
    savedRef.current = { title: t.trim(), description: d, dueDate: due };
    versionRef.current = card.data.version;
    seededFor.current = cardId;
    forceReseed.current = false;
  }, [card.data, cardId]);

  // Escape closes the drawer, but not while a confirm dialog is up, which owns Escape itself.
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
    mutationFn: (vals: CardFields) =>
      updateCard(boardId, cardId, versionRef.current, {
        title: vals.title,
        description: vals.description.trim() ? vals.description : null,
        // The API takes a DateTimeOffset; a bare date has no time, so pin it to UTC midnight.
        dueDate: vals.dueDate ? new Date(`${vals.dueDate}T00:00:00Z`).toISOString() : null,
      }),
    onSuccess: (updated, vals) => {
      versionRef.current = updated.version;
      savedRef.current = vals;
      setConflict(null);
      invalidateBoard();
    },
    onError: (err) => {
      // 412: the card moved under us. Never retry without a fresh If-Match, since overwriting
      // someone else's edit is what the version check exists to prevent. Show the current version.
      if (err instanceof ApiError && err.status === 412) {
        setConflict("Someone else changed this card while you were editing. Your changes were not saved; the current version is shown below.");
        resaveWanted.current = false;
        forceReseed.current = true;
        void card.refetch();
      }
      // Other failures (409/428/network) surface through the inline error and keep the user's text.
    },
    onSettled: () => {
      inFlight.current = false;
      if (resaveWanted.current) {
        resaveWanted.current = false;
        doSaveRef.current();
      }
    },
  });

  const doSave = () => {
    const v = latestRef.current;
    const s = savedRef.current;
    if (!card.data || !writable || !v.title || !s) return;
    if (v.title === s.title && v.description === s.description && v.dueDate === s.dueDate) return;
    if (inFlight.current) {
      resaveWanted.current = true;
      return;
    }
    inFlight.current = true;
    save.mutate(v);
  };

  // Callbacks and listeners that outlive a render (the settle handler, the tab-hide listener) call
  // through this so they always run the current doSave, not a stale closure from an earlier render.
  const doSaveRef = useRef(doSave);
  doSaveRef.current = doSave;

  // Autosave text edits a beat after typing stops. Typing resets the timer, so a save fires only
  // once the field is quiet, not on every keystroke.
  useEffect(() => {
    if (!dirty) return;
    const timer = setTimeout(doSave, 1500);
    return () => clearTimeout(timer);
    // doSave reads refs, so it does not need to be a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [title, description, dirty]);

  // Flush on tab hide so an edit is not stranded when the user switches away or closes the tab.
  // Best-effort: a save started as the tab closes may not finish.
  useEffect(() => {
    const onHide = () => {
      if (document.visibilityState === "hidden") doSaveRef.current();
    };
    document.addEventListener("visibilitychange", onHide);
    window.addEventListener("pagehide", onHide);
    return () => {
      document.removeEventListener("visibilitychange", onHide);
      window.removeEventListener("pagehide", onHide);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const assign = useMutation({
    mutationFn: (assigneeId: string | null) => assignCard(boardId, cardId, assigneeId),
    onSuccess: (updated) => {
      // Assignment bumps the row version too; adopt it so the next card save is not a false conflict.
      if (updated?.version) versionRef.current = updated.version;
      invalidateBoard();
    },
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

  // The board's limits, so the UI can say them before you pick a file rather than after.
  const usage = useQuery({
    queryKey: boardSettingsKeys.usage(boardId),
    queryFn: () => getBoardUsage(boardId),
  });

  const invalidateAttachments = () => {
    void queryClient.invalidateQueries({ queryKey: contentKeys.attachments(boardId, cardId) });
    // Attachment count and stored bytes changed, so refresh the storage bar too.
    void queryClient.invalidateQueries({ queryKey: boardSettingsKeys.usage(boardId) });
  };

  const upload = useMutation({
    mutationFn: (file: File) => uploadAttachment(boardId, cardId, file),
    onSuccess: invalidateAttachments,
    // The server's message already names the numbers, so there is nothing to translate: show it.
    onError: (err) => show(err instanceof Error ? err.message : "The upload failed."),
  });

  /**
   * Refuse an oversized file client-side, without asking the server. The API is the real gate and
   * would reject it too; this is not a security control. But a limit you discover only after a
   * round trip is a worse experience than one you are told about immediately.
   */
  const pick = (file: File) => {
    const max = usage.data?.maxFileBytes;
    if (max !== undefined && file.size > max) {
      show(`“${file.name}” is ${formatBytes(file.size)}. The limit is ${formatBytes(max)} per file.`);
      return;
    }
    upload.mutate(file);
  };

  const removeAttachment = useMutation({
    mutationFn: (attachmentId: string) => deleteAttachment(boardId, cardId, attachmentId),
    onSuccess: invalidateAttachments,
  });

  const download = async (attachmentId: string) => {
    // The API mints a short-lived presigned URL and the browser fetches the bytes straight from
    // storage; they never pass through the API.
    const { downloadUrl } = await getDownloadUrl(boardId, cardId, attachmentId);
    window.open(downloadUrl, "_blank", "noopener");
  };

  // Ctrl+V anywhere in the drawer uploads pasted files. Only intercept when the clipboard actually
  // carries files, so pasting text into a field still works normally.
  const onPaste = (e: ClipboardEvent) => {
    if (!writable) return;
    const files = Array.from(e.clipboardData.files ?? []);
    if (files.length === 0) return;
    e.preventDefault();
    files.forEach(pick);
  };

  const assigneeOptions = [
    { value: "", label: "Unassigned" },
    ...members.map((m) => ({
      value: m.userId,
      label: m.displayName,
      icon: <Avatar email={m.email} name={m.displayName} size="sm" />,
    })),
  ];

  // The creator is resolved from the board's members, the same way the assignee is. A creator who
  // has since left the board is no longer in that list, so fall back to a neutral label.
  const creator = members.find((m) => m.userId === card.data?.createdById);

  return (
    <div className="drawer-backdrop" onClick={onClose}>
      <aside
        className="drawer"
        onClick={(e) => e.stopPropagation()}
        onPaste={onPaste}
        role="dialog"
        aria-label="Card"
      >
        <header className="drawer-head">
          <h2>Card</h2>
          <div className="row">
            {writable && (
              <button
                className="icon-btn"
                type="button"
                aria-label={save.isPending ? "Saving" : dirty ? "Save changes" : "All changes saved"}
                title={save.isPending ? "Saving…" : dirty ? "Save changes" : "All changes saved"}
                disabled={!dirty || !title.trim() || save.isPending}
                onClick={doSave}
              >
                {save.isPending ? (
                  <Loader2 size={16} className="spin" />
                ) : dirty ? (
                  <Save size={16} className="pop" />
                ) : (
                  <Check size={16} className="pop" />
                )}
              </button>
            )}
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

              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  doSave();
                }}
                className="stack"
              >
                <input
                  className="card-title-input"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  onBlur={doSave}
                  disabled={!writable}
                  maxLength={500}
                  aria-label="Card title"
                />
                <label>
                  Description
                  <textarea
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    onBlur={doSave}
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
                      {/* Read from the saved card, not the picker beside it: the flag reports what
                          everyone else sees, not a date still being chosen. */}
                      {isOverdue(card.data.dueDate) && (
                        <span className="flag">
                          <AlertTriangle size={10} />
                          Overdue
                        </span>
                      )}
                    </span>
                    <DatePicker
                      value={dueDate}
                      disabled={!writable}
                      ariaLabel="Due date"
                      onChange={(v) => {
                        setDueDate(v);
                        // A pick is a deliberate discrete change, so save it now rather than on the
                        // text debounce. Update latestRef synchronously so doSave sees the new date.
                        latestRef.current = { ...latestRef.current, dueDate: v };
                        doSave();
                      }}
                    />
                  </label>
                  <label className="grow">
                    Assignee
                    <Dropdown
                      value={card.data.assigneeId ?? ""}
                      options={assigneeOptions}
                      disabled={!writable}
                      ariaLabel="Assignee"
                      onChange={(v) => assign.mutate(v || null)}
                    />
                  </label>
                </div>
                {members.length > 0 && (
                  <p className="faint card-created-by">
                    <span>Created by</span>
                    {creator ? (
                      <>
                        <Avatar email={creator.email} name={creator.displayName} size="sm" />
                        <strong>{creator.displayName}</strong>
                      </>
                    ) : (
                      <strong>a former member</strong>
                    )}
                  </p>
                )}
                {save.isError && !conflict && <p className="error">{(save.error as Error).message}</p>}
              </form>

              <section>
                <h3>Attachments</h3>
                {writable && (
                  <label className="file-drop">
                    <Paperclip size={15} />
                    {upload.isPending
                      ? "Uploading…"
                      : usage.data
                        ? `Attach a file — up to ${formatBytes(usage.data.maxFileBytes)}`
                        : "Attach a file"}
                    <input
                      type="file"
                      onChange={(e) => {
                        const file = e.target.files?.[0];
                        if (file) pick(file);
                        e.target.value = "";
                      }}
                    />
                  </label>
                )}
                {writable && <p className="faint">Tip: paste a file or image with Ctrl+V.</p>}
                {/* Upload errors go to a toast (pick / upload.onError), so no inline copy here. */}

                {attachments.data?.map((a) => (
                  <div key={a.id} className="attachment">
                    <AttachmentThumb boardId={boardId} cardId={cardId} attachment={a} />
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
                    className="comment-composer"
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
                      aria-label="Write a comment"
                    />
                    <button
                      className="icon-btn"
                      type="submit"
                      aria-label="Send comment"
                      title="Send"
                      disabled={!body.trim() || addComment.isPending}
                    >
                      <SendHorizontal size={16} />
                    </button>
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
